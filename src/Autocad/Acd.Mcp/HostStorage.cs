using System;
using System.IO;

namespace Acd.Mcp
{
    // The one place that names the plugin's storage folders. Each host gets
    // its own tree: a script or DTO written for BricsCAD (Teigha namespaces,
    // no Civil 3D) does not compile in AutoCAD and vice versa, so saved
    // scripts, user DTOs, editor mirrors and run history must never mix.
    //
    //   Local   (%LOCALAPPDATA%\<AppFolder>\) — machine state: log, config,
    //           dto-system, editor mirrors, batch-runs.
    //   Roaming (%APPDATA%\<AppFolder>\)      — user-owned: scripts, dto-user.
    internal static class HostStorage
    {
#if BRICSCAD
        public const string AppFolder = "Bcad.Mcp";
#else
        public const string AppFolder = "Acd.Mcp";
#endif

        public static string LocalRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder);

        public static string RoamingRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);

        public static string Local(string name) => Path.Combine(LocalRoot, name);

        public static string Roaming(string name) => Path.Combine(RoamingRoot, name);
    }
}
