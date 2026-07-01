using System.IO;
using System.Reflection;

namespace PunkPlayerHighlight
{
    internal static class ModFolder
    {
        internal static string Dir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    }
}
