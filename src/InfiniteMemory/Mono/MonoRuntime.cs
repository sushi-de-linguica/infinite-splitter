using System;
using System.Collections.Generic;
using InfiniteSplitter.Memory;

namespace InfiniteSplitter.Mono
{
    /// <summary>
    /// Resolves managed names to addresses in a running Mono process by walking
    /// Mono's own metadata. Read-only: no injection, nothing written.
    ///
    ///   mono module -> exports -> mono_get_root_domain's body -> MonoDomain
    ///     -> domain_assemblies (GSList) -> the target MonoAssembly -> MonoImage
    ///     -> class_cache hash table -> MonoClass by name
    ///     -> field array -> MonoClassField by name (instance offsets)
    ///     -> runtime_info -> MonoVTable -> static field storage (static offsets)
    ///
    /// Every step validates what it read, so a wrong offset produces ClassNotFound
    /// or LayoutMismatch rather than a plausible wrong answer.
    /// </summary>
    public sealed class MonoRuntime
    {
        public const string MonoModuleName = "mono-2.0-bdwgc.dll";

        private readonly IMemory _memory;

        public long ModuleBase { get; private set; }
        public long RootDomain { get; private set; }
        public int DomainId { get; private set; }
        public long Assembly { get; private set; }
        public long Image { get; private set; }
        public string ImageName { get; private set; } = "";
        public string ImageFileName { get; private set; } = "";

        public MonoOffsetProbe Probe { get; } = new MonoOffsetProbe();
        public Dictionary<string, long> Exports { get; private set; } = new Dictionary<string, long>();

        /// <summary>Bucket arrays bigger than this are not believed.</summary>
        private const int MaxBuckets = 1 << 16;

        /// <summary>Which of <see cref="MonoOffsets.ClassNameCandidates"/> actually worked.</summary>
        public int ResolvedClassNameOffset { get; private set; } = MonoOffsets.ClassName;

        /// <summary>Where the class-cache bucket-array pointer actually lives, as an
        /// offset into the MonoImage. Differs from the compiled-in constant when the
        /// search had to find it.</summary>
        public int ResolvedClassCacheOffset { get; private set; } = MonoOffsets.ImageClassCacheTable;

        /// <summary>Every class in the target image, by name. A name can repeat across
        /// namespaces, hence the list per name.</summary>
        private readonly Dictionary<string, List<long>> _classesByName =
            new Dictionary<string, List<long>>(StringComparer.Ordinal);

        public int ClassCount { get; private set; }

        public MonoRuntime(IMemory memory) { _memory = memory; }

        /// <summary>
        /// Full resolve. Runs once, when the process hook fires; everything it
        /// produces stays valid until the game exits (bdwgc never moves objects).
        /// </summary>
        public Result Attach(long monoModuleBase, string assemblyName)
        {
            ModuleBase = monoModuleBase;

            var exports = PeExports.Parse(_memory, monoModuleBase);
            if (exports.IsErr) return exports.Discard();
            Exports = exports.Value;

            Probe.Decode(_memory, Exports);

            var domain = ResolveRootDomain();
            if (domain.IsErr) return domain.Discard();
            RootDomain = domain.Value;
            DomainId = _memory.ReadInt32(RootDomain + Probe.DomainId);
            if (DomainId < 0 || DomainId > 0xFFFF)
                return Result.Err(RefreshErrorReason.LayoutMismatch, $"implausible domain id {DomainId}");

            var image = ResolveImage(assemblyName);
            if (image.IsErr) return image;

            return BuildClassCache();
        }

        private Result<long> ResolveRootDomain()
        {
            if (!Exports.TryGetValue("mono_get_root_domain", out long accessor))
                return Result<long>.Err(RefreshErrorReason.MonoExportMissing, "mono_get_root_domain");

            // `mov rax,[rip+disp32]; ret` — the displacement gives the slot's address.
            var body = _memory.ReadBytes(accessor, 8);
            if (!MonoOffsetProbe.TryDecodeRipRelative(body, accessor, out long slot))
                return Result<long>.Err(RefreshErrorReason.LayoutMismatch,
                    "mono_get_root_domain is not the expected `mov rax,[rip+disp32]; ret`: " + Hex(body));

            long domain = _memory.ReadPointer(slot);
            if (domain == 0)
                return Result<long>.Err(RefreshErrorReason.RootDomainNull, "the runtime has not finished starting");

            return Result<long>.Ok(domain);
        }

        private Result ResolveImage(string assemblyName)
        {
            long node = _memory.ReadPointer(RootDomain + MonoOffsets.DomainAssemblies);
            var seen = new HashSet<long>();
            var found = new List<string>();

            while (node != 0 && seen.Add(node) && seen.Count < 4096)
            {
                long assembly = _memory.ReadPointer(node + MonoOffsets.GSListData);
                if (assembly != 0)
                {
                    long image = _memory.ReadPointer(assembly + Probe.AssemblyImage);
                    if (image != 0)
                    {
                        string name = _memory.ReadAsciiString(_memory.ReadPointer(image + Probe.ImageAssemblyName), 256);
                        if (MemoryExtensions.LooksLikeIdentifier(name)) found.Add(name);

                        if (string.Equals(name, assemblyName, StringComparison.Ordinal))
                        {
                            Assembly = assembly;
                            Image = image;
                            ImageName = name;
                            ImageFileName = _memory.ReadAsciiString(_memory.ReadPointer(image + Probe.ImageFileName), 512);
                            return Result.Ok();
                        }
                    }
                }
                node = _memory.ReadPointer(node + MonoOffsets.GSListNext);
            }

            if (found.Count == 0)
                return Result.Err(RefreshErrorReason.LayoutMismatch,
                    "walked the domain assembly list and read no assembly names — domain_assemblies (0x" +
                    MonoOffsets.DomainAssemblies.ToString("X") + ") is probably wrong for this Mono build");

            return Result.Err(RefreshErrorReason.AssemblyNotFound,
                assemblyName + " is not loaded yet (" + found.Count + " assemblies present)");
        }

        /// <summary>
        /// Walks the image's class-cache hash table into a name -> class map.
        ///
        /// Every bucket, rather than a hash lookup: Mono's typedef hash function is an
        /// implementation detail we would have to reimplement exactly, and one pass
        /// over the image's few hundred classes costs milliseconds, once.
        /// </summary>
        private Result BuildClassCache()
        {
            var classes = CollectClasses(_memory.ReadPointer(Image + MonoOffsets.ImageClassCacheTable));

            if (classes.Count == 0)
            {
                // The compiled-in offset did not pan out, so search for the bucket
                // array instead. It is unmistakable: every non-null entry is a
                // MonoClass whose image points back at the image we started from.
                long found = LocateClassCacheTable();
                if (found == 0)
                    return Result.Err(RefreshErrorReason.LayoutMismatch,
                        $"could not find {ImageName}'s class cache anywhere in the first 0x800 bytes of " +
                        $"the MonoImage (compiled-in ImageClassCacheTable is 0x{MonoOffsets.ImageClassCacheTable:X})");

                ResolvedClassCacheOffset = (int)(found - Image);
                classes = CollectClasses(_memory.ReadPointer(found));
                if (classes.Count == 0)
                    return Result.Err(RefreshErrorReason.LayoutMismatch,
                        $"found a class-cache candidate at +0x{ResolvedClassCacheOffset:X} but it yielded no classes");
            }

            if (!ResolveClassNameOffset(classes))
                return Result.Err(RefreshErrorReason.LayoutMismatch,
                    $"found {classes.Count} classes but none of the candidate name offsets " +
                    "yielded readable identifiers");

            _classesByName.Clear();
            foreach (var klass in classes)
            {
                string name = _memory.ReadAsciiString(_memory.ReadPointer(klass + ResolvedClassNameOffset), 256);
                if (!MemoryExtensions.LooksLikeIdentifier(name)) continue;

                if (!_classesByName.TryGetValue(name, out var list))
                    _classesByName[name] = list = new List<long>();
                list.Add(klass);
            }

            ClassCount = classes.Count;
            return Result.Ok();
        }

        /// <summary>
        /// Walks a bucket array into the list of classes belonging to this image.
        ///
        /// The array length is never read: iteration stops at the first entry that is
        /// neither null nor a MonoClass of this image. That drops one unverified
        /// offset, and makes a wrong table address yield an empty list, not nonsense.
        /// </summary>
        private List<long> CollectClasses(long table)
        {
            var classes = new List<long>();
            if (table == 0) return classes;

            int consecutiveNulls = 0;
            for (int bucket = 0; bucket < MaxBuckets; bucket++)
            {
                long klass = _memory.ReadPointer(table + bucket * 8);

                if (klass == 0)
                {
                    // A long run of zeros means we walked off the end of the array.
                    if (++consecutiveNulls > 8192) break;
                    continue;
                }

                if (_memory.ReadPointer(klass + Probe.ClassImage) != Image) break;
                consecutiveNulls = 0;

                int chain = 0;
                while (klass != 0 && chain++ < 4096)
                {
                    if (_memory.ReadPointer(klass + Probe.ClassImage) != Image) break;
                    classes.Add(klass);
                    klass = _memory.ReadPointer(klass + MonoOffsets.ClassNextClassCache);
                }
            }
            return classes;
        }

        /// <summary>
        /// Finds the class-cache bucket array by scanning the MonoImage struct for a
        /// pointer to one: several non-null entries, every one a MonoClass whose image
        /// field points back here. Nothing else in a MonoImage looks like that.
        ///
        /// Returns the address of the pointer field, not of the table itself.
        /// </summary>
        private long LocateClassCacheTable()
        {
            long best = 0;
            int bestCount = 0;

            for (int offset = 0; offset < 0x800; offset += 8)
            {
                long table = _memory.ReadPointer(Image + offset);
                if (table < 0x10000) continue;

                int hits = 0;
                for (int bucket = 0; bucket < 256; bucket++)
                {
                    long klass = _memory.ReadPointer(table + bucket * 8);
                    if (klass == 0) continue;
                    if (_memory.ReadPointer(klass + Probe.ClassImage) != Image) { hits = -1; break; }
                    hits++;
                }

                if (hits < 4) continue;

                int count = CollectClasses(table).Count;
                if (count > bestCount) { bestCount = count; best = Image + offset; }
            }

            return best;
        }

        /// <summary>
        /// MonoClass.name could not be measured offline — its accessor is wrapped in a
        /// GC-unsafe thunk that does not decode. Try the candidate set and keep the
        /// offset where most classes yield a printable identifier.
        /// </summary>
        private bool ResolveClassNameOffset(List<long> classes)
        {
            int sample = Math.Min(classes.Count, 64);
            int bestOffset = -1, bestScore = 0;

            foreach (int candidate in MonoOffsets.ClassNameCandidates)
            {
                int score = 0;
                for (int i = 0; i < sample; i++)
                {
                    string name = _memory.ReadAsciiString(_memory.ReadPointer(classes[i] + candidate), 128);
                    if (MemoryExtensions.LooksLikeIdentifier(name)) score++;
                }
                if (score > bestScore) { bestScore = score; bestOffset = candidate; }
                if (score == sample) break;      // perfect; no need to try the rest
            }

            if (bestOffset < 0 || bestScore * 100 < sample * 80) return false;

            ResolvedClassNameOffset = bestOffset;
            return true;
        }

        /// <summary>
        /// Describes a class by address rather than by name. This is how generic
        /// instantiations are reached: a `List&lt;T&gt;` is not in the image's class
        /// cache, but a live instance points at its vtable, and that at its class.
        /// Reading the layout off the object's own class is what keeps corlib offsets
        /// from ever being hardcoded.
        /// </summary>
        public Result<MonoClassRef> DescribeClass(long klassAddress)
        {
            if (klassAddress == 0)
                return Result<MonoClassRef>.Err(RefreshErrorReason.ClassNotFound, "null class pointer");

            var result = new MonoClassRef
            {
                Address = klassAddress,
                Name = _memory.ReadAsciiString(_memory.ReadPointer(klassAddress + ResolvedClassNameOffset), 256),
            };

            var fields = ReadFields(result);
            if (fields.IsErr) return Result<MonoClassRef>.Err(fields.Error);

            ResolveStatics(result);
            return Result<MonoClassRef>.Ok(result);
        }

        /// <summary>The class of a live object, via its vtable. MonoObject's first field
        /// is its MonoVTable, whose first field is its MonoClass.</summary>
        public Result<MonoClassRef> DescribeObject(long objectAddress)
        {
            if (objectAddress == 0)
                return Result<MonoClassRef>.Err(RefreshErrorReason.ClassNotFound, "null object");

            long vtable = _memory.ReadPointer(objectAddress);
            if (vtable == 0)
                return Result<MonoClassRef>.Err(RefreshErrorReason.LayoutMismatch, "object has no vtable");

            return DescribeClass(_memory.ReadPointer(vtable + MonoOffsets.VTableKlass));
        }

        public Result<MonoClassRef> FindClass(string name, string nameSpace = "")
        {
            if (!_classesByName.TryGetValue(name, out var candidates) || candidates.Count == 0)
                return Result<MonoClassRef>.Err(RefreshErrorReason.ClassNotFound,
                    $"no class named '{name}' in {ImageName} (walked {ClassCount} classes)");

            long chosen = candidates[0];
            if (candidates.Count > 1)
            {
                int nameSpaceOffset = ResolvedClassNameOffset + (MonoOffsets.ClassNameSpace - MonoOffsets.ClassName);
                foreach (var klass in candidates)
                {
                    string ns = _memory.ReadAsciiString(_memory.ReadPointer(klass + nameSpaceOffset), 256);
                    if (string.Equals(ns, nameSpace, StringComparison.Ordinal)) { chosen = klass; break; }
                }
            }

            var result = new MonoClassRef
            {
                Address = chosen,
                Name = name,
                Namespace = nameSpace,
            };

            var fields = ReadFields(result);
            if (fields.IsErr) return Result<MonoClassRef>.Err(fields.Error);

            ResolveStatics(result);      // absence of statics is not an error
            return Result<MonoClassRef>.Ok(result);
        }

        private Result ReadFields(MonoClassRef klass)
        {
            long fields = _memory.ReadPointer(klass.Address + MonoOffsets.ClassFields);
            if (fields == 0) return Result.Ok();                 // legitimately field-less

            // MonoClass has a different tail per class_kind, so field_count only lives
            // at 0x100 for an ordinary class; for a generic instantiation such as
            // HashSet<string> the value there is not a count at all. Treat it as a
            // hint — the parent check below is the real terminator.
            int count = _memory.ReadInt32(klass.Address + MonoOffsets.ClassFieldCount);
            if (count <= 0 || count > 4096) count = 512;

            // Every entry in a field array points back at one class, and it must be
            // this one — or, for a generic instantiation, the generic definition it was
            // inflated from: HashSet<string>'s fields belong to HashSet<T>, so
            // demanding pointer identity would reject every generic type in the game.
            long expectedParent = _memory.ReadPointer(fields + Probe.FieldParent);
            if (expectedParent != klass.Address)
            {
                string parentName = expectedParent == 0 ? "" :
                    _memory.ReadAsciiString(_memory.ReadPointer(expectedParent + ResolvedClassNameOffset), 256);

                if (string.IsNullOrEmpty(klass.Name) || parentName != klass.Name)
                    return Result.Err(RefreshErrorReason.LayoutMismatch,
                        $"{klass.Name}'s first field belongs to '{parentName}' — " +
                        $"ClassFields (0x{MonoOffsets.ClassFields:X}) is probably wrong");
            }

            for (int i = 0; i < count; i++)
            {
                long field = fields + i * MonoOffsets.FieldSize;

                if (_memory.ReadPointer(field + Probe.FieldParent) != expectedParent) break;

                string name = _memory.ReadAsciiString(_memory.ReadPointer(field + Probe.FieldName), 256);
                if (!MemoryExtensions.LooksLikeIdentifier(name)) continue;

                long type = _memory.ReadPointer(field + MonoOffsets.FieldType);
                var reference = new MonoFieldRef
                {
                    Name = name,
                    Offset = _memory.ReadInt32(field + MonoOffsets.FieldOffset),
                    TypePointer = type,
                    Address = field,
                    Kind = type == 0 ? MonoTypeKind.End : (MonoTypeKind)_memory.ReadByte(type + Probe.TypeType),
                    IsStatic = type != 0 &&
                               (_memory.ReadUInt16(type + Probe.TypeAttrs) & MonoOffsets.FieldAttributeStatic) != 0,
                };

                klass.FieldList.Add(reference);
                klass.Fields[name] = reference;
            }

            return Result.Ok();
        }

        /// <summary>
        /// The static field storage, transcribed from mono_vtable_get_static_field_data:
        ///
        ///   vtable      = klass->runtime_info->domain_vtables[domain_id]
        ///   staticData  = vtable[0x48 + 8 * klass->vtable_size]   (if flags &amp; 4)
        ///
        /// Leaving StaticData at 0 is normal: the class has no statics, or its static
        /// constructor has not run yet. RunLogger is routinely in that state.
        /// </summary>
        private void ResolveStatics(MonoClassRef klass)
        {
            long runtimeInfo = _memory.ReadPointer(klass.Address + MonoOffsets.ClassRuntimeInfo);
            if (runtimeInfo == 0) return;

            int maxDomain = _memory.ReadUInt16(runtimeInfo + MonoOffsets.RuntimeInfoMaxDomain);
            if (maxDomain < DomainId) return;

            long vtable = _memory.ReadPointer(runtimeInfo + MonoOffsets.RuntimeInfoDomainVTables + 8 * DomainId);
            if (vtable == 0) return;

            if (_memory.ReadPointer(vtable + MonoOffsets.VTableKlass) != klass.Address) return;
            klass.VTable = vtable;

            byte flags = _memory.ReadByte(vtable + MonoOffsets.VTableFlags);
            if ((flags & MonoOffsets.VTableFlagHasStaticFields) == 0) return;

            int vtableSize = _memory.ReadInt32(klass.Address + MonoOffsets.ClassVTableSize);
            if (vtableSize < 0 || vtableSize > 65536) return;

            klass.StaticData = _memory.ReadPointer(vtable + MonoOffsets.VTableBase + 8 * vtableSize);
        }

        /// <summary>Class names in the target image, sorted. Diagnostics only.</summary>
        public List<string> ClassNames()
        {
            var names = new List<string>(_classesByName.Keys);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static string Hex(byte[] bytes)
        {
            var parts = new string[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) parts[i] = bytes[i].ToString("x2");
            return string.Join(" ", parts);
        }
    }
}
