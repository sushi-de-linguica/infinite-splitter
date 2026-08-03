using System.Runtime.CompilerServices;

// The offline harness builds fixtures by driving GameSnapshot's setters directly.
// They stay internal so nothing in the component can fake a snapshot by accident.
[assembly: InternalsVisibleTo("splitcheck")]
