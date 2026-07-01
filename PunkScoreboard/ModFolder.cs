using System.IO;
using System.Reflection;

namespace PunkScoreboard
{
    internal static class ModFolder
    {
        internal static string Dir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    }
}
