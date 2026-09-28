using System.IO;

namespace Acd.Mcp.Serialization
{
    // Canonical filesystem locations for DTO storage. Two tiers:
    //
    //   dto-system  in HostStorage.Local("dto-system")
    //     Owned by the plugin install. Wiped and repopulated on startup.
    //     The user must not edit these — changes are lost on next install.
    //
    //   dto-user    in HostStorage.Roaming("dto-user")
    //     Owned by the user. The plugin never writes here. A same-typed file
    //     here overrides whatever the system folder ships for that type.
    //
    // The split exists so the installer can refresh the shipped DTO set
    // without ever clobbering a user's customisation.
    public static class DtoPaths
    {
        public const string SystemFolderName = "dto-system";
        public const string UserFolderName = "dto-user";

        public static string SystemFolder { get; } = HostStorage.Local(SystemFolderName);

        public static string UserFolder { get; } = HostStorage.Roaming(UserFolderName);

        public static void EnsureFolders()
        {
            Directory.CreateDirectory(SystemFolder);
            Directory.CreateDirectory(UserFolder);
        }
    }
}
