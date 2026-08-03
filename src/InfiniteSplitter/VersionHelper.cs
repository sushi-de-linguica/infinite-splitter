using System;
using System.Diagnostics;
using System.Reflection;

namespace InfiniteSplitter
{
    /// <summary>The assembly's own version, read at runtime so the csproj stays the
    /// one place it is declared.</summary>
    public static class VersionHelper
    {
        private static Version _version;

        public static Version Version
        {
            get
            {
                if (_version != null) return _version;
                try
                {
                    var location = Assembly.GetExecutingAssembly().Location;
                    var info = FileVersionInfo.GetVersionInfo(location);
                    _version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
                }
                catch
                {
                    _version = new Version(0, 0, 0);
                }
                return _version;
            }
        }

        public static string Display => Version.ToString(3);
    }
}
