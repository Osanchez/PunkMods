using System.IO;
using System.Reflection;

namespace PunkFourPlayer
{
    /// <summary>This DLL's own folder (e.g. BepInEx/plugins/PunkFourPlayer/) — the home for its
    /// config and any future assets.</summary>
    internal static class ModFolder
    {
        internal static string Dir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    }
}
