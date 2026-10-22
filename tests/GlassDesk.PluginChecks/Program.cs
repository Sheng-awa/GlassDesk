using System.IO.Compression;
using System.Text.Json;
using GlassDesk.Core;
using GlassDesk.Plugins;

int checks=0;
void Check(bool value,string label){if(!value)throw new Exception("FAIL "+label);checks++;Console.WriteLine("PASS "+label);}
string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
string work=Path.Combine(root,"artifacts","plugin-checks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
string package=Path.Combine(work,"example.glassdesk-plugin");
void MakePackage(string version="1.0.0",bool traversal=false,string? entryType=null)
{
    if(File.Exists(package))File.Delete(package);
    using var archive=ZipFile.Open(package,ZipArchiveMode.Create);
    var manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(root,"samples","StatusPlugin","plugin.json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    using(var output=new StreamWriter(archive.CreateEntry("plugin.json").Open()))output.Write(JsonSerializer.Serialize(manifest with{Version=version,EntryType=entryType??manifest.EntryType}));
    archive.CreateEntryFromFile(Path.Combine(root,"samples","StatusPlugin","bin","Release","net10.0","StatusPlugin.dll"),"StatusPlugin.dll");
    if(traversal)using(new StreamWriter(archive.CreateEntry("../escape.txt").Open())){ }
}
MakePackage();var dispatcher=new InlineDispatcher();
await using var host=new PluginHost(dispatcher,work);
int companionUpdates=0;host.CompanionChanged+=state=>{if(state is not null)companionUpdates++;};
var preferences=new WidgetPreferences();
await host.ApplyPreferencesAsync(preferences);
Check(host.Statuses.Single(p=>p.Id=="glassdesk.companion") is {Enabled:true,State:PluginRunState.Running},"builtin companion defaults enabled and running");
Check(host.Statuses.Single(p=>p.Id=="glassdesk.liquidglass") is {Enabled:false,State:PluginRunState.Disabled},"builtin liquid glass defaults disabled");
int cycles=args.Contains("--no-stress",StringComparer.Ordinal)?2:100;
for(int i=0;i<cycles;i++){await host.ApplyPreferencesAsync(preferences with{CompanionEnabled=false,LiquidGlassEnabled=true});await host.ApplyPreferencesAsync(preferences);}
Check(companionUpdates>=cycles&&host.Statuses.Single(p=>p.Id=="glassdesk.companion").State==PluginRunState.Running,$"{cycles} independent builtin enable/disable cycles complete");
await host.UpdateSceneAsync(new(WidgetForm.Capsule,false,MediaSnapshot.Disconnected,false,1));
Check(host.Statuses.Single(p=>p.Id=="glassdesk.companion") is {Enabled:true,State:PluginRunState.Paused},"hidden pauses work without changing enabled intent");
await host.UpdateSceneAsync(new(WidgetForm.Capsule,true,MediaSnapshot.Disconnected,false,2));
Check(host.Statuses.Single(p=>p.Id=="glassdesk.companion").State==PluginRunState.Running,"visible resumes plugin without accumulated animation");
string id=await host.ImportAsync(package);preferences=preferences with{PluginEnabled=new(){[id]=true}};await host.ApplyPreferencesAsync(preferences);
Check(host.Pages.Single().Items[0].CommandId==id+"/increment"&&host.Settings(id).Count==1,"local plugin exposes content page command and setting");
await host.ExecuteAsync(id,"increment");
Check(host.GetConfiguration(id)!.Value.GetProperty("count").GetInt32()==1,"plugin command writes its independent configuration");
MakePackage("1.1.0");await host.ImportAsync(package);
Check(host.Statuses.Single(p=>p.Id==id).Version=="1.1.0"&&host.GetConfiguration(id)!.Value.GetProperty("count").GetInt32()==1,"same-id update replaces version and preserves configuration");
await host.ExecuteAsync(id,"increment");
Check(host.GetConfiguration(id)!.Value.GetProperty("count").GetInt32()==2,"updated load context shares the real contract types");
MakePackage("1.2.0",entryType:"GlassDesk.Sample.MissingEntry");
bool updateRejected=false;try{await host.ImportAsync(package);}catch(InvalidOperationException){updateRejected=true;}
Check(updateRejected&&host.Statuses.Single(p=>p.Id==id) is {Version:"1.1.0",State:PluginRunState.Running}
    &&host.GetConfiguration(id)!.Value.GetProperty("count").GetInt32()==2,
    "failed enabled update restores previous running plugin and preserves its configuration");
await host.ExecuteAsync(id,"increment");
Check(host.GetConfiguration(id)!.Value.GetProperty("count").GetInt32()==3,"rolled-back plugin remains functional after a rejected update");
try{await host.ExecuteAsync(id,"throw");}catch(InvalidOperationException){ }
Check(host.Statuses.Single(p=>p.Id==id).State==PluginRunState.Faulted&&host.Pages.Count==0&&host.Statuses.Single(p=>p.Id=="glassdesk.companion").State==PluginRunState.Running,"plugin exception retracts contributions while host and other plugins survive");
await host.RemoveAsync(id);
Check(!host.Statuses.Any(p=>p.Id==id)&&File.Exists(Path.Combine(work,"plugin-config",id+".json")),"remove unloads plugin and retains independent configuration");
MakePackage(traversal:true);bool rejected=false;try{await host.ImportAsync(package);}catch(InvalidDataException){rejected=true;}
Check(rejected&&!File.Exists(Path.Combine(work,"plugins","escape.txt")),"package traversal rejected before escaped file can be created");
await AdditionalChecks.RunAsync(root,Check);
await LegacyBinaryCompatibilityChecks.RunAsync(root,Check);
Console.WriteLine($"{checks} checks passed.");

sealed class InlineDispatcher : IUiDispatcher
{
    public bool TryEnqueue(Action action){action();return true;}
    public Task<T> InvokeAsync<T>(Func<Task<T>> action)=>action();
}
