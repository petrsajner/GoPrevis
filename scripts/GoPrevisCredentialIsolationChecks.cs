using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task RunCredentialIsolationChecksAndQuit()
    {try{await RunCredentialIsolationChecks();GetTree().Quit(0);}catch(Exception e){GD.PushError("CREDENTIAL_ISOLATION_FAILED "+e);GetTree().Quit(1);}}
    async Task RunCredentialIsolationChecks()
    {
        string folder=ProjectSettings.GlobalizePath("res://output/credential-isolation-checks/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(folder);int checks=0;
        void Check(bool yes,string message){Require(yes,"Credential isolation: "+message);checks++;GD.Print("PASS Credential isolation: "+message);}
        foreach(string provider in new[]{"openrouter","openai","gemini"})
        {
            var targets=AiCredentials.CredentialTargets(provider);Check(targets.Length==2&&targets.All(t=>t.StartsWith(provider+".com.petrsajner.",StringComparison.Ordinal)),provider+" reads only this product's current/previous credential names");
        }
        string settings=Path.Combine(folder,"legacy-settings.json");File.WriteAllText(settings,"{\"Selection\":4,\"DefaultsRevision\":2,\"UseVividia\":true}");var loaded=AiConnectionSettings.Load(settings);loaded.Save(settings);
        Check(loaded.Model=="openai/gpt-6.1-sol"&&!File.ReadAllText(settings).Contains("UseVividia",StringComparison.Ordinal),"retired external-source preferences are ignored and disappear on save");
        OpenAiDirector();for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);OpenAiConnection();for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        var buttons=Descendants(_aiConnectionDialog!).OfType<Button>().Select(b=>b.Text).ToArray();
        Check(buttons.Contains("Save connection")&&!buttons.Any(t=>t.Contains("existing",StringComparison.OrdinalIgnoreCase)||t.Contains("VIVIDIA",StringComparison.OrdinalIgnoreCase)),"connection UI exposes only the user's provider-key workflow");
        Check(_aiKeyInput.Secret&&_aiKeyInput.Text=="","connection settings never prefill a stored key into the UI");
        Check(!_aiConnectionStatus.Text.Contains("VIVIDIA",StringComparison.OrdinalIgnoreCase),"director status has no external application dependency");
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"own-api-key-connection.png"));_aiConnectionDialog!.Hide();_aiDialog!.Hide();
        File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status="passed",checks,noApiCalls=true},_json));GD.Print("CREDENTIAL_ISOLATION_OK checks="+checks+" output="+folder);
    }
}
