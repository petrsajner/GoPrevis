using Godot;

namespace GenPrevis;

public partial class GenPrevisApp
{
    static string AppVersion=>ProjectSettings.GetSetting("application/config/version","0.14.0").AsString();
    const string AppCopyright="© Petr Sajner 2026";
}
