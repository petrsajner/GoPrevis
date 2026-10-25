using Godot;

namespace GoPrevis;

public partial class GoPrevisApp
{
    static string AppVersion=>ProjectSettings.GetSetting("application/config/version","0.17.0").AsString();
    const string AppCopyright="© Petr Sajner 2026";
}
