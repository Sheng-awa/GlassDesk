using System.Text.Json;
using GlassDesk.Core;
using GlassDesk.NativeBridge;

var checks=new List<string>();
void Check(bool condition,string message)
{
    if(!condition)throw new InvalidOperationException(message);
    checks.Add(message);Console.WriteLine("PASS "+message);
}
var expected=new Dictionary<string,int>{{"GDOp.size",64},{"GDOpV2.size",96},{"fontWeight",64},{"trackingEm",68},
    {"textRole",72},{"lineHeightDip",76},{"baselineDip",80},{"flags",84},{"locale",88}};
Check(expected.All(p=>NativeTypographyAbi.ManagedLayout[p.Key]==p.Value),"managed V1 and V2 sizes and offsets match the frozen x64 ABI");
Check(Enum.GetValues<SurfaceOpKind>().Select(x=>(int)x).SequenceEqual(Enumerable.Range(0,8)),"legacy paint operation numbers remain unchanged");
var legacy=new SurfaceOp{Text="old plugin",Bold=true};
Check(legacy.FontWeight==0&&legacy.TextRole==TextRole.Legacy&&legacy.LineHeightDip==0&&legacy.TextFlags==TextFlags.None&&legacy.Bold,
    "old paint commands retain Bold and default Legacy typography");
var mixed=SurfaceOp.Typography("天球(そら)のMúsica",TextRole.MediaDetailsTitle,0,0,200,24,0xff20252c);
var latin=SurfaceOp.Typography("GlassDesk Music",TextRole.MediaDetailsTitle,0,0,200,28,0xff20252c);
Check(mixed.FontSize==20&&mixed.FontWeight==600&&mixed.TrackingEm==0&&mixed.LineHeightDip==24&&mixed.BaselineDip==18.75f&&latin.TrackingEm==-.01f&&latin.BaselineDip==0,
    "role commands preserve fixed slots and restrict negative tracking to Latin headings");
var clock=SurfaceOp.Typography("11:11:11",TextRole.ClockCapsule,0,0,200,32,0xff20252c);
var date=SurfaceOp.Typography("2026年10月18日",TextRole.WidgetDateValue,0,0,200,56,0xff20252c);
var digits=SurfaceOp.Typography("GPU 27%",TextRole.PageTitle,0,0,200,28,0xff20252c,numericFlags:TextFlags.TabularDigitsOnly);
Check(clock.TextFlags==TextFlags.TabularNumbers&&date.TextFlags==TextFlags.TabularDigitsOnly&&digits.TrackingEm==0,
    "time and digit-only date fields carry semantic numeric features across the bridge");
bool old=args.Contains("--expect-v1-rejection",StringComparer.Ordinal);
NativeTypographyDescription? native=null;
if(old)
{
    bool rejected=false;
    try{NativeTypographyAbi.ReadAndValidate();}
    catch(InvalidOperationException error) when(error.InnerException is EntryPointNotFoundException){rejected=true;}
    Check(rejected,"an old native DLL is rejected explicitly instead of silently discarding V2 fields");
}
else
{
    native=NativeTypographyAbi.ReadAndValidate();
    Check(native.Value.Version==2&&native.Value.V1Size==64&&native.Value.V2Size==96&&(native.Value.Capabilities&15)==15,
        "the loaded native DLL negotiates rendering, measurement, profile and cache diagnostics");
}
string folder=Path.GetFullPath("artifacts/qa/typography-widgets-preview2");Directory.CreateDirectory(folder);
File.WriteAllText(Path.Combine(folder,old?"managed-abi-old-dll.json":"managed-abi-current-dll.json"),
    JsonSerializer.Serialize(new{Passed=checks.Count,Managed=NativeTypographyAbi.ManagedLayout,Native=native,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{checks.Count} checks passed.");
