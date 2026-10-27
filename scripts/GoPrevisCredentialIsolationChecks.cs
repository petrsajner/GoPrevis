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
        // Vividia's keys (0.20.0): read only where Vividia keeps them, only when chosen.
        Check(AiCredentials.VividiaTarget("openrouter")=="gis_openrouter_apikey.com.vividia.personal-providers.v1"&&AiCredentials.VividiaTarget("gemini")=="gis_apikey.com.vividia.personal-providers.v1"&&AiCredentials.VividiaTarget("openai")==null,"Vividia's OpenRouter and Gemini entries are the only shared names; OpenAI has none");
        string settings=Path.Combine(folder,"vividia-settings.json");File.WriteAllText(settings,"{\"Selection\":4,\"DefaultsRevision\":2,\"UseVividia\":true}");var loaded=AiConnectionSettings.Load(settings);loaded.Save(settings);
        Check(loaded.Model=="openai/gpt-6.1-sol"&&loaded.UseVividia&&AiConnectionSettings.Load(settings).UseVividia,"the choice to use the Vividia key survives load and save");
        bool vividiaKey=AiCredentials.VividiaHas("openrouter"),ownKey=AiCredentials.Read("openrouter")!=null;
        if(vividiaKey&&!ownKey)Check(AiCredentials.Has("openrouter",true)&&!AiCredentials.Has("openrouter",false),"Vividia's key is read only when the Vividia key is chosen");
        else Check(AiCredentials.Has("openrouter",true)==(ownKey||vividiaKey),"the shared read adds only Vividia's key ("+(vividiaKey?"an own key takes precedence":"no Vividia key on this computer")+")");
        bool previous=AiSettings.UseVividia;
        OpenAiDirector();for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);OpenAiConnection();for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        var buttons=Descendants(_aiConnectionDialog!).OfType<Button>().Select(b=>b.Text).ToArray();
        bool offered=AiCredentials.VividiaHas(AiSettings.Provider)||AiSettings.UseVividia;
        Check(buttons.Contains("Save connection")&&buttons.Any(t=>t.Contains("Vividia",StringComparison.Ordinal))==offered,"the Vividia key is offered exactly when Vividia keeps one for this provider ("+(offered?"offered":"not offered")+")");
        Check(_aiKeyInput.Secret&&_aiKeyInput.Text=="","connection settings never prefill a stored key into the UI");
        AiSettings.UseVividia=true;RefreshAiConnection();string withVividia=_aiConnectionStatus.Text;AiSettings.UseVividia=previous;RefreshAiConnection();
        Check(!(AiCredentials.VividiaHas(AiSettings.Provider)&&AiCredentials.Read(AiSettings.Provider)==null)||withVividia.Contains("Vividia's key",StringComparison.Ordinal),"the status names Vividia's key when it is the one in use");
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"own-api-key-connection.png"));_aiConnectionDialog!.Hide();_aiDialog!.Hide();
        File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status="passed",checks,noApiCalls=true},_json));GD.Print("CREDENTIAL_ISOLATION_OK checks="+checks+" output="+folder);
    }
}
