using System;
using System.Reflection;

namespace POSGardenia.Services
{
    // The version of this build. It is set in one place: <Version> in POSGardenia.csproj.
    public static class AppInfo
    {
        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
            }
        }

        public static string VersionText => $"Version {Version}";
    }
}
