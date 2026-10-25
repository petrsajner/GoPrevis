using Godot;

namespace Previs;

public partial class PrevisApp
{
    static string AppVersion=>ProjectSettings.GetSetting("application/config/version","0.13.0").AsString();
    const string AppCopyright="© Petr Sajner 2026";
}
