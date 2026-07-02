using System.IO;
using System.Reflection;

namespace PunkDamageSlowMo
{
    internal static class ModFolder
    {
        internal static string Dir
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                var loc = asm.Location;
                // Normal install: the DLL's own folder. Dev hot-reload loads the assembly from bytes
                // (Location is empty) -> fall back to plugins/<AssemblyName> so config still lands in
                // the mod's real folder instead of throwing on Path.Combine(null, ...).
                return string.IsNullOrEmpty(loc)
                    ? Path.Combine(BepInEx.Paths.PluginPath, asm.GetName().Name)
                    : Path.GetDirectoryName(loc);
            }
        }
    }
}
