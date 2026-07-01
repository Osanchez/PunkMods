using System.IO;
using System.Reflection;

namespace PunkReviveItem
{
    internal static class ModFolder
    {
        internal static string Dir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    }
}
