using System.IO;
using System.Reflection;

namespace PunkSeedPicker
{
    internal static class ModFolder
    {
        internal static string Dir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    }
}
