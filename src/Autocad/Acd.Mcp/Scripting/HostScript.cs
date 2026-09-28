namespace Acd.Mcp.Scripting
{
    // The one place that knows which CAD host the runtime-compiled C# (REPL
    // submissions, batch bodies, DTO .csx files) is compiled for: the host API
    // namespaces every compile imports, and the preprocessor symbol that lets a
    // single script or DTO carry both hosts' code.
    //
    // AutoCAD and BricsCAD expose the same API under different namespaces
    // (Autodesk.AutoCAD.* vs Bricscad.* / Teigha.*), so a script written against
    // the implicit imports works in both; one that names a namespace explicitly
    // branches with `#if BRICSCAD`.
    internal static class HostScript
    {
#if BRICSCAD
        public const string ApplicationServices = "Bricscad.ApplicationServices";
        public const string DatabaseServices = "Teigha.DatabaseServices";
        public const string Geometry = "Teigha.Geometry";
        public const string EditorInput = "Bricscad.EditorInput";
        public const string Runtime = "Teigha.Runtime";

        // BricsCAD has no Civil 3D API.
        public static readonly string[] CivilNamespaces = [];

        // Roslyn's scripting API takes no parse options, so the symbol goes in
        // as a directive. `#line 1` keeps diagnostics on the caller's lines.
        public static string Prepare(string code) => "#define BRICSCAD\n#line 1\n" + code;

        // DTO headers name their type the AutoCAD way (`// @dto:
        // Autodesk.AutoCAD.DatabaseServices.Line`) so one .csx serves both
        // hosts; this is the same type under BricsCAD's namespaces.
        public static string MapTypeName(string autocadName)
        {
            foreach (var (acad, host) in TypeNamespaceMap)
            {
                if (autocadName.StartsWith(acad, System.StringComparison.Ordinal))
                    return host + autocadName.Substring(acad.Length);
            }
            return autocadName;
        }

        private static readonly (string Acad, string Host)[] TypeNamespaceMap =
        [
            ("Autodesk.AutoCAD.ApplicationServices.", ApplicationServices + "."),
            ("Autodesk.AutoCAD.DatabaseServices.", DatabaseServices + "."),
            ("Autodesk.AutoCAD.Geometry.", Geometry + "."),
            ("Autodesk.AutoCAD.EditorInput.", EditorInput + "."),
            ("Autodesk.AutoCAD.Runtime.", Runtime + "."),
            ("Autodesk.AutoCAD.Colors.", "Teigha.Colors."),
            ("Autodesk.AutoCAD.GraphicsInterface.", "Teigha.GraphicsInterface."),
        ];
#else
        public const string ApplicationServices = "Autodesk.AutoCAD.ApplicationServices";
        public const string DatabaseServices = "Autodesk.AutoCAD.DatabaseServices";
        public const string Geometry = "Autodesk.AutoCAD.Geometry";
        public const string EditorInput = "Autodesk.AutoCAD.EditorInput";
        public const string Runtime = "Autodesk.AutoCAD.Runtime";

        public static readonly string[] CivilNamespaces =
        [
            "Autodesk.Civil",
            "Autodesk.Civil.ApplicationServices",
            "Autodesk.Civil.DatabaseServices",
            "Autodesk.Civil.DatabaseServices.Styles",
        ];

        public static string Prepare(string code) => code;

        public static string MapTypeName(string autocadName) => autocadName;
#endif
    }
}
