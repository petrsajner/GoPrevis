using Godot;

namespace GoPrevis;

public partial class GoPrevisApp
{
    static string AppVersion=>ProjectSettings.GetSetting("application/config/version","0.18.1").AsString();
    const string AppCopyright="© Petr Sajner 2026";
}
