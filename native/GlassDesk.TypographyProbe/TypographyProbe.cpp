#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d2d1_1.h>
#include <wincodec.h>
#include "../GlassDesk.Native/TypographyEngine.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <iterator>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

using winrt::com_ptr;
using winrt::check_hresult;
using namespace gd::typography;
namespace fs=std::filesystem;
namespace
{
std::string Utf8(std::wstring const& text)
{
    if(text.empty())return {};
    int count=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),int(text.size()),nullptr,0,nullptr,nullptr);
    if(!count)throw winrt::hresult_invalid_argument();std::string result(size_t(count),'\0');
    if(!WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),int(text.size()),result.data(),count,nullptr,nullptr))throw winrt::hresult_invalid_argument();return result;
}
std::string Json(std::string const& text)
{
    std::ostringstream result;result<<'"';
    for(unsigned char value:text)
    {
        if(value=='"'||value=='\\')result<<'\\'<<char(value);
        else if(value=='\n')result<<"\\n";else if(value=='\r')result<<"\\r";else if(value=='\t')result<<"\\t";
        else if(value<32)result<<"\\u"<<std::hex<<std::setw(4)<<std::setfill('0')<<int(value)<<std::dec;
        else result<<char(value);
    }
    result<<'"';return result.str();
}
std::string Json(std::wstring const& text){return Json(Utf8(text));}
void Save(fs::path const& path,std::string const& text)
{std::ofstream stream(path,std::ios::binary);stream.exceptions(std::ios::badbit|std::ios::failbit);stream<<text;}
fs::path CheckedOutput(fs::path const& requested)
{
    wchar_t module[32768]{};DWORD length=GetModuleFileNameW(nullptr,module,UINT32(std::size(module)));
    if(!length||length>=std::size(module))throw std::runtime_error("Cannot locate the probe executable.");
    auto project=fs::path(module);for(int i=0;i<5;++i)project=project.parent_path();
    auto expected=project/L"artifacts/native-ui-preview/0.3.0-preview.2/typography-probe";
    if(!fs::equivalent(fs::path(module).parent_path(),expected)||!fs::is_regular_file(project/L"src/GlassDesk.Core/TypographyCatalog.cs"))
        throw std::runtime_error("Run the probe from its dedicated GlassDesk preview.2 build output.");
    auto allowed=fs::absolute(project/L"artifacts/qa/typography-widgets-preview2/font-study").lexically_normal();
    auto output=fs::absolute(requested).lexically_normal();auto a=allowed.begin(),b=output.begin();
    for(;a!=allowed.end();++a,++b)
        if(b==output.end()||CompareStringOrdinal(a->c_str(),-1,b->c_str(),-1,TRUE)!=CSTR_EQUAL)
            throw std::runtime_error("--out must be a new child directory of the preview.2 font-study QA directory.");
    if(b==output.end())throw std::runtime_error("--out must be a new child directory, not font-study itself.");
    for(auto ancestor=output;!ancestor.empty();)
    {
        auto attributes=GetFileAttributesW(ancestor.c_str());
        if(attributes!=INVALID_FILE_ATTRIBUTES&&(attributes&FILE_ATTRIBUTE_REPARSE_POINT))
            throw std::runtime_error("Font-study output must not traverse a reparse point.");
        if(CompareStringOrdinal(ancestor.c_str(),-1,project.c_str(),-1,TRUE)==CSTR_EQUAL)break;
        auto parent=ancestor.parent_path();if(parent==ancestor)break;ancestor=parent;
    }
    if(fs::exists(output)&&(!fs::is_directory(output)||!fs::is_empty(output)))
        throw std::runtime_error("Refusing to overwrite an existing font-study run; choose a new --out child directory.");
    return output;
}
std::string ProfileName(Profile profile)
{return profile==Profile::Baseline?"baseline":profile==Profile::SystemA?"system-a":"noto-qa";}
std::wstring Localized(IDWriteLocalizedStrings* names)
{
    if(!names||!names->GetCount())return {};
    UINT32 index{},length{};BOOL exists{};check_hresult(names->FindLocaleName(L"en-us",&index,&exists));if(!exists)index=0;
    check_hresult(names->GetStringLength(index,&length));std::wstring value(size_t(length)+1,L'\0');check_hresult(names->GetString(index,value.data(),length+1));value.resize(length);return value;
}
std::string Tag(DWRITE_FONT_AXIS_TAG tag)
{auto value=uint32_t(tag);std::string result;for(int i=0;i<4;++i)result+=char((value>>(i*8))&255);return result;}

class Diagnostics final : public IDWriteTextRenderer
{
    std::atomic<ULONG> references{1};
    float pixelsPerDip;
public:
    std::vector<std::string> runs;
    uint32_t simulatedRuns{},zeroGlyphs{};
    explicit Diagnostics(float dpi):pixelsPerDip(dpi/96){}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** value)override
    {
        if(!value)return E_POINTER;*value=nullptr;
        if(iid==__uuidof(IUnknown)||iid==__uuidof(IDWriteTextRenderer)||iid==__uuidof(IDWritePixelSnapping)){*value=static_cast<IDWriteTextRenderer*>(this);AddRef();return S_OK;}return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef()override{return ++references;}
    ULONG STDMETHODCALLTYPE Release()override{auto value=--references;if(!value)delete this;return value;}
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*,BOOL* value)override{if(!value)return E_POINTER;*value=TRUE;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetCurrentTransform(void*,DWRITE_MATRIX* value)override{if(!value)return E_POINTER;*value={1,0,0,1,0,0};return S_OK;}
    HRESULT STDMETHODCALLTYPE GetPixelsPerDip(void*,FLOAT* value)override{if(!value)return E_POINTER;*value=pixelsPerDip;return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawGlyphRun(void*,FLOAT x,FLOAT y,DWRITE_MEASURING_MODE mode,DWRITE_GLYPH_RUN const* glyph,DWRITE_GLYPH_RUN_DESCRIPTION const* description,IUnknown*)override
    {
        try
        {
            if(!glyph||!glyph->fontFace)return E_INVALIDARG;
            com_ptr<IDWriteFontFace> face;face.copy_from(glyph->fontFace);auto face3=face.try_as<IDWriteFontFace3>();auto face5=face.try_as<IDWriteFontFace5>();
            std::wstring family,faceName;int actualWeight=-1;
            if(face3){com_ptr<IDWriteLocalizedStrings> names;check_hresult(face3->GetFamilyNames(names.put()));family=Localized(names.get());names=nullptr;check_hresult(face3->GetFaceNames(names.put()));faceName=Localized(names.get());actualWeight=int(face3->GetWeight());}
            DWRITE_FONT_METRICS metrics{};face->GetMetrics(&metrics);float em=glyph->fontEmSize/float(metrics.designUnitsPerEm);
            float advance{};UINT32 missing{};for(UINT32 i=0;i<glyph->glyphCount;++i){advance+=glyph->glyphAdvances?glyph->glyphAdvances[i]:0;missing+=glyph->glyphIndices[i]==0?1u:0u;}
            simulatedRuns+=face->GetSimulations()!=DWRITE_FONT_SIMULATIONS_NONE?1u:0u;zeroGlyphs+=missing;
            std::ostringstream row;row<<std::fixed<<std::setprecision(6);
            row<<"{\"textPosition\":"<<(description?description->textPosition:0)<<",\"textLengthUtf16\":"<<(description?description->stringLength:0)
                <<",\"text\":"<<Json(description?std::wstring(description->string,description->stringLength):L"")
                <<",\"locale\":"<<Json(description&&description->localeName?description->localeName:L"")
                <<",\"family\":"<<Json(family)<<",\"face\":"<<Json(faceName)<<",\"faceWeightActual\":";
            if(actualWeight>=0)row<<actualWeight;else row<<"null";
            row<<",\"simulations\":"<<uint32_t(face->GetSimulations())<<",\"faceIndex\":"<<face->GetIndex()<<",\"fontEmSizeDip\":"<<glyph->fontEmSize
                <<",\"baselineXDip\":"<<x<<",\"baselineYDip\":"<<y<<",\"runAdvanceDip\":"<<advance
                <<",\"ascentDip\":"<<metrics.ascent*em<<",\"descentDip\":"<<metrics.descent*em<<",\"lineGapDip\":"<<metrics.lineGap*em
                <<",\"glyphCount\":"<<glyph->glyphCount<<",\"zeroGlyphCount\":"<<missing<<",\"missingGlyphAssessment\":\"review-zero-glyph-clusters\",\"bidiLevel\":"<<glyph->bidiLevel
                <<",\"sideways\":"<<(glyph->isSideways?"true":"false")<<",\"measuringMode\":"<<uint32_t(mode)
                <<",\"axisApiSupported\":"<<(face5?"true":"false")<<",\"hasVariations\":"<<(face5&&face5->HasVariations()?"true":"false")<<",\"axes\":[";
            bool first=true,opsz=false,weightAxis=false;
            if(face5)
            {
                UINT32 count=face5->GetFontAxisValueCount();std::vector<DWRITE_FONT_AXIS_VALUE> values(count);if(count)check_hresult(face5->GetFontAxisValues(values.data(),count));
                com_ptr<IDWriteFontResource> resource;check_hresult(face5->GetFontResource(resource.put()));UINT32 rangeCount=resource->GetFontAxisCount();std::vector<DWRITE_FONT_AXIS_RANGE> ranges(rangeCount);if(rangeCount)check_hresult(resource->GetFontAxisRanges(ranges.data(),rangeCount));
                for(auto const& axis:values)
                {
                    if(!first)row<<',';first=false;auto found=std::find_if(ranges.begin(),ranges.end(),[&](auto const& range){return range.axisTag==axis.axisTag;});
                    opsz|=axis.axisTag==DWRITE_FONT_AXIS_TAG_OPTICAL_SIZE;weightAxis|=axis.axisTag==DWRITE_FONT_AXIS_TAG_WEIGHT;
                    row<<"{\"tag\":"<<Json(Tag(axis.axisTag))<<",\"actualValue\":"<<axis.value<<",\"minimum\":";
                    if(found!=ranges.end())row<<found->minValue;else row<<"null";row<<",\"maximum\":";if(found!=ranges.end())row<<found->maxValue;else row<<"null";
                    row<<",\"variable\":"<<(found!=ranges.end()&&found->maxValue>found->minValue?"true":"false")<<'}';
                }
            }
            row<<"],\"opszSupported\":"<<(face5?(opsz?"true":"false"):"null")<<",\"wghtSupported\":"<<(face5?(weightAxis?"true":"false"):"null")
                <<",\"unsupportedAxes\":"<<(!face5?"null":opsz?"[]":"[\"opsz\"]")<<",\"glyphIndices\":[";
            for(UINT32 i=0;i<glyph->glyphCount;++i){if(i)row<<',';row<<glyph->glyphIndices[i];}
            row<<"],\"glyphAdvancesDip\":[";
            for(UINT32 i=0;i<glyph->glyphCount;++i){if(i)row<<',';if(glyph->glyphAdvances)row<<glyph->glyphAdvances[i];else row<<"null";}
            row<<"],\"clusterMap\":[";
            if(description&&description->clusterMap)for(UINT32 i=0;i<description->stringLength;++i){if(i)row<<',';row<<description->clusterMap[i];}
            auto face2=face.try_as<IDWriteFontFace2>();row<<"],\"faceHasColorGlyphs\":"<<(face2?(face2->IsColorFont()?"true":"false"):"null")<<",\"fontFiles\":[";
            UINT32 fileCount{};check_hresult(face->GetFiles(&fileCount,nullptr));std::vector<IDWriteFontFile*> files(fileCount);if(fileCount)check_hresult(face->GetFiles(&fileCount,files.data()));
            std::vector<com_ptr<IDWriteFontFile>> ownedFiles;for(auto file:files){com_ptr<IDWriteFontFile> owned;owned.attach(file);ownedFiles.push_back(owned);}
            for(UINT32 i=0;i<fileCount;++i)
            {
                if(i)row<<',';com_ptr<IDWriteFontFileLoader> loader;check_hresult(files[i]->GetLoader(loader.put()));auto local=loader.try_as<IDWriteLocalFontFileLoader>();
                if(!local){row<<"null";continue;}void const* key{};UINT32 keySize{},length{};check_hresult(files[i]->GetReferenceKey(&key,&keySize));check_hresult(local->GetFilePathLengthFromKey(key,keySize,&length));
                std::wstring path(size_t(length)+1,L'\0');check_hresult(local->GetFilePathFromKey(key,keySize,path.data(),length+1));path.resize(length);row<<Json(path);
            }
            row<<"]}";runs.push_back(row.str());return S_OK;
        }catch(...){return winrt::to_hresult();}
    }
    HRESULT STDMETHODCALLTYPE DrawUnderline(void*,FLOAT,FLOAT,DWRITE_UNDERLINE const*,IUnknown*)override{return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawStrikethrough(void*,FLOAT,FLOAT,DWRITE_STRIKETHROUGH const*,IUnknown*)override{return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawInlineObject(void* context,FLOAT x,FLOAT y,IDWriteInlineObject* object,BOOL sideways,BOOL rtl,IUnknown* effect)override
    {return object?object->Draw(context,this,x,y,sideways,rtl,effect):E_INVALIDARG;}
};

struct Sample {std::wstring id,text,locale;uint32_t flags{};};
std::vector<Sample> Samples()
{
    return {
        {L"cjk-controls",L"时间 日期 设置 播放 暂停",L"zh-CN"},
        {L"today-mixed",L"今天 08:08 · GlassDesk",L"zh-CN"},
        {L"windows-mixed",L"Windows 11 — 音乐",L"zh-CN"},
        {L"shanghai-mixed",L"上海 / Shanghai",L"zh-CN"},
        {L"song-japanese",L"天球(そら)のMúsica",L"ja-JP"},
        {L"numeric-percent-temperature",L"GPU 27% · 24°C",L"en-US",TabularNumbers},
        {L"time-short",L"00:00  11:11  23:59",L"en-US",TabularNumbers},
        {L"time-seconds",L"08:08:08  11:11:11",L"en-US",TabularNumbers},
        {L"kana",L"かな・カナ / 日本語",L"ja-JP"},
        {L"hangul",L"한국어 / Hangul",L"ko-KR"},
        {L"combining",L"e\u0301 A\u0308 / Café / Música",L"en-US"},
        {L"emoji-zwj",L"\U0001f469\U0001f3fd\u200d\U0001f4bb  \U0001f642  \u2764\ufe0f\u200d\U0001f525 / Emoji",L"en-US"},
        {L"bidi",L"العربية ١٢:٣٤ / עברית 12:34",L"ar-SA",TabularDigitsOnly},
        {L"latin-heading",L"GlassDesk — Windows Music Companion",L"en-US"},
        {L"traditional",L"時間 日期 設定 播放 暫停",L"zh-TW"},
        {L"supplementary-digits",L"日期 𝟙𝟚 / ١٢ / 12",L"zh-CN",TabularDigitsOnly}
    };
}
Request MakeRequest(Sample const& sample,float size,uint32_t weight,float width=1090,float height=72)
{
    Request value;value.text=sample.text;value.locale=sample.locale;value.role=size>=20?9u:size<=12?6u:7u;
    value.fontSize=size;value.fontWeight=weight;value.cjkWeight=weight>=600?500u:400u;value.flags=sample.flags;
    value.trackingEm=size>=20?-.01f:0;value.lineHeight=size>=36?size+8:size>=20?size+8:size+6;
    value.width=width;value.height=height;value.trim=false;value.paragraphCenter=false;return value;
}
std::string Describe(Engine& engine,Request const& request,std::wstring const& id)
{
    auto layout=engine.CreateLayout(request);com_ptr<Diagnostics> diagnostics;diagnostics.attach(new Diagnostics(request.dpi));
    check_hresult(layout->textLayout->Draw(nullptr,diagnostics.get(),0,0));auto const& metrics=layout->metrics;
    std::ostringstream row;row<<std::fixed<<std::setprecision(6);
    DWRITE_OVERHANG_METRICS overhang{};check_hresult(layout->textLayout->GetOverhangMetrics(&overhang));
    row<<"{\"id\":"<<Json(id)<<",\"profile\":"<<Json(ProfileName(layout->profile))<<",\"tokenRevision\":"<<layout->revision
        <<",\"role\":"<<request.role<<",\"text\":"<<Json(layout->text)<<",\"fontSizeDip\":"<<request.fontSize
        <<",\"dpi\":"<<request.dpi<<",\"requestedFamily\":"<<Json(layout->requestedFamily)<<",\"requestedLatinWeight\":"<<layout->latinWeight
        <<",\"requestedCjkWeight\":"<<layout->cjkWeight<<",\"callerRequestedWeight\":"<<request.fontWeight
        <<",\"localeRequested\":"<<Json(request.locale)<<",\"localeResolved\":"<<Json(layout->resolvedLocale)
        <<",\"opticalRole\":"<<Json(layout->optical==Optical::Small?"Small":layout->optical==Optical::Text?"Text":"Display")
        <<",\"automaticOpticalAxisRequested\":"<<(layout->opticalAxisRequested?"true":"false")<<",\"trackingEmApplied\":"<<layout->trackingEm
        <<",\"textFlagsApplied\":"<<layout->flags<<",\"widthConstraintDip\":"<<request.width<<",\"heightConstraintDip\":"<<request.height
        <<",\"advanceDip\":"<<metrics.advance<<",\"widthIncludingTrailingWhitespaceDip\":"<<metrics.widthIncludingTrailingWhitespace
        <<",\"heightDip\":"<<metrics.height<<",\"baselineDip\":"<<metrics.baseline<<",\"lineHeightDip\":"<<metrics.lineHeight<<",\"lineCount\":"<<metrics.lineCount
        <<",\"overhangDip\":{\"left\":"<<overhang.left<<",\"top\":"<<overhang.top<<",\"right\":"<<overhang.right<<",\"bottom\":"<<overhang.bottom<<"},\"scriptFormatting\":[";
    for(size_t i=0;i<layout->scripts.size();++i)
    {if(i)row<<',';auto const& script=layout->scripts[i];row<<"{\"start\":"<<script.start<<",\"lengthUtf16\":"<<script.length<<",\"isoScriptCode\":"<<script.isoScriptCode<<",\"isoScriptNumber\":"<<script.isoScriptNumber<<",\"requestedWeight\":"<<script.requestedWeight<<",\"locale\":"<<Json(script.locale)<<'}';}
    row<<"],\"simulationFallbacks\":[";
    for(size_t i=0;i<layout->simulationFallbacks.size();++i)
    {if(i)row<<',';auto const& repair=layout->simulationFallbacks[i];row<<"{\"start\":"<<repair.start<<",\"lengthUtf16\":"<<repair.length<<",\"requestedWeight\":"<<repair.requestedWeight<<",\"selectedRealWeight\":"<<repair.selectedRealWeight<<",\"family\":"<<Json(repair.family)<<'}';}
    row<<"],\"runs\":[";
    for(size_t i=0;i<diagnostics->runs.size();++i){if(i)row<<',';row<<diagnostics->runs[i];}row<<"]}";return row.str();
}
struct SheetRow {Request request;std::wstring label;};
struct FixedSlot
{
    std::wstring id,group,source;
    Request request;
    float x{},y{},shellWidth{},shellHeight{};
};
bool FixedSlotLatin(std::wstring const& text)
{
    return !text.empty()&&std::all_of(text.begin(),text.end(),[](wchar_t character)
    {auto value=uint32_t(character);return value<=0x024f||(value>=0x2000&&value<=0x206f);});
}
Request FixedSlotRequest(std::wstring const& text,std::wstring const& locale,uint32_t role,float width,float height,bool center=false,uint32_t flags=UINT32_MAX)
{
    auto const& token=TokenForRole(role);Request request;request.text=text;request.locale=locale;request.role=role;
    request.fontSize=token.size;request.fontWeight=token.latinWeight;
    // GDOpV2 does not carry cjkWeight: the engine derives the catalog value.
    request.lineHeight=std::min(token.lineHeight,height);request.width=width;request.height=height;
    // Matches TypographyCatalog.BaselineForSlot and SurfaceOp.Typography. The
    // P1 catalog token keeps its default baseline/28 DIP line unchanged.
    if(role==9&&height==24)request.baseline=18.75f;
    request.flags=flags==UINT32_MAX?token.flags:flags;
    request.trackingEm=request.flags||!FixedSlotLatin(text)?0:token.trackingEm;
    request.center=center;request.paragraphCenter=true;request.trim=true;return request;
}
std::vector<FixedSlot> FixedPlayerSlots()
{
    std::vector<FixedSlot> result;
    auto add=[&](std::wstring const& id,std::wstring const& group,std::wstring const& source,std::wstring const& text,std::wstring const& locale,
        uint32_t role,float x,float y,float width,float height,float shellWidth,float shellHeight,bool center=false,uint32_t flags=UINT32_MAX)
    {result.push_back({id,group,source,FixedSlotRequest(text,locale,role,width,height,center,flags),x,y,shellWidth,shellHeight});};
    std::vector<Sample> titles{
        {L"zh",L"夜空中的陪伴 · 音乐播放器",L"zh-CN"},
        {L"ja",L"可愛くなりたい (feat. 鎖那)",L"ja-JP"},
        {L"latin",L"GlassDesk — A Little Light at Midnight",L"en-US"},
        {L"mixed",L"天球(そら)のMúsica / 上海 Shanghai",L"ja-JP"},
        {L"emoji",L"\U0001f469\U0001f3fd\u200d\U0001f4bb \U0001f642 \u2764\ufe0f\u200d\U0001f525 音乐 / Music",L"zh-CN"},
        {L"arabic",L"موسيقى الليل ١٢:٣٤ — رفيق صغير",L"ar-SA"},
        {L"hebrew",L"מוזיקת לילה 12:34 — חבר קטן",L"he-IL"},
        {L"long",L"这是一首非常非常长的歌名 — A Very Long Song Title / 長い曲名 (Live at Midnight, Extended Edition)",L"zh-CN"}
    };
    // Source: UpdateMetadata. Capsule shell is clamped to 208..296 DIP;
    // text width is shellWidth - 60 - (audio ? 52 : 12), not a fixed 200 DIP.
    for(float shellWidth:{208.0f,296.0f})for(bool audio:{true,false})for(auto const& sample:titles)
    {
        float width=shellWidth-60-(audio?52:12);auto variant=L"capsule-"+std::to_wstring(int(shellWidth))+(audio?L"-audio":L"-quiet");
        auto source=L"WidgetScene.UpdateMetadata: metadata layer (60,9), height36; audio="+std::wstring(audio?L"true":L"false");
        add(variant+L"-title-"+sample.id,L"capsule",source,sample.text,sample.locale,5,60,9,width,20,shellWidth,52);
        add(variant+L"-artist-"+sample.id,L"capsule",source,sample.text,sample.locale,6,60,29,width,16,shellWidth,52);
    }
    for(float width:{164.0f,200.0f})for(auto const& sample:titles)
    {
        auto variant=L"peek-"+std::to_wstring(int(width));
        add(variant+L"-title-"+sample.id,L"peek",L"WidgetScene.UpdateMetadata: layer (96,56), height50",sample.text,sample.locale,7,96,56,width,26,320,176);
        add(variant+L"-artist-"+sample.id,L"peek",L"WidgetScene.UpdateMetadata: layer (96,56), height50",sample.text,sample.locale,8,96,86,width,20,320,176);
    }
    for(auto const& sample:titles)
    {
        add(L"details-title-"+sample.id,L"details",L"WidgetScene.UpdateMetadata: layer (28,216), title height24 clamps catalog lineHeight28 to24; explicit slot baseline18.75",sample.text,sample.locale,9,28,216,304,24,360,476,true);
        add(L"details-artist-"+sample.id,L"details",L"WidgetScene.UpdateMetadata: layer (28,216), artist local y28",sample.text,sample.locale,10,28,244,304,20,360,476,true);
    }
    for(auto text:{L"00:00",L"11:11",L"23:59"})
    {
        std::wstring time=text;
        for(bool companion:{true,false})for(bool secondary:{true,false})
        {
            float left=companion?56.0f:16.0f;auto id=L"clock-capsule-"+std::wstring(companion?L"companion":L"alone")+(secondary?L"-secondary-":L"-single-")+time;
            add(id,L"clock-numeric",L"WidgetScene.DrawCapsule: width200; clock role25DIP, height27/32",time,L"en-US",1,left,secondary?5.0f:10.0f,200-left-16,secondary?27.0f:32.0f,200,52,true,TabularNumbers);
        }
        add(L"clock-whiteplate-"+time,L"clock-numeric",L"WidgetScene.DrawPeek: text slot272x26 over protected plate (68,14,184,32)",time,L"en-US",2,24,18,272,26,320,176,true,TabularNumbers);
        add(L"clock-peek-hero-"+time,L"clock-numeric",L"WidgetScene.DrawPeek disconnected: text192x44",time,L"en-US",4,24,30,192,44,240,144,true,TabularNumbers);
    }
    add(L"clock-capsule-date",L"clock-numeric",L"WidgetScene.DrawCapsule: secondary date/weekday 128x16",L"10月18日  星期日",L"zh-CN",3,56,32,128,16,200,52,true);
    add(L"clock-whiteplate-date",L"clock-numeric",L"WidgetScene.DrawPeek: secondary date/weekday272x12",L"10月18日  星期日",L"zh-CN",3,24,42,272,12,320,176,true);
    add(L"clock-hero-date",L"clock-numeric",L"WidgetScene.DrawPeek disconnected: secondary192x21",L"10月18日  星期日",L"zh-CN",15,24,77,192,21,240,144,true);
    for(auto text:{L"0:00",L"1:11",L"8:08",L"9:59",L"11:11",L"59:59",L"1:00:00",L"11:11:11"})
    {
        std::wstring time=text;
        add(L"position-"+time,L"clock-numeric",L"WidgetScene.UpdateTimeline: Caption with explicit TabularNumbers, timeline-local (0,20)",time,L"en-US",15,0,20,112,18,248,38,false,TabularNumbers);
        add(L"duration-"+time,L"clock-numeric",L"WidgetScene.UpdateTimeline: Caption with explicit TabularNumbers, timeline-local (136,20)",time,L"en-US",15,136,20,112,18,248,38,true,TabularNumbers);
    }
    return result;
}
struct FixedSlotAudit
{
    uint32_t samples{},verticalOverflow{},horizontalOverflow{},trimmed{},simulatedRuns{},zeroGlyphs{};
    float maximumTopOverflow{},maximumBottomOverflow{};
};
std::string DescribeFixedSlot(Engine& engine,FixedSlot const& slot,float dpi,FixedSlotAudit& audit)
{
    auto request=slot.request;request.dpi=dpi;auto layout=engine.CreateLayout(request);
    DWRITE_TEXT_METRICS metrics{};DWRITE_OVERHANG_METRICS overhang{};check_hresult(layout->textLayout->GetMetrics(&metrics));check_hresult(layout->textLayout->GetOverhangMetrics(&overhang));
    UINT32 lineCount{};layout->textLayout->GetLineMetrics(nullptr,0,&lineCount);std::vector<DWRITE_LINE_METRICS> lines(lineCount);
    if(lineCount)check_hresult(layout->textLayout->GetLineMetrics(lines.data(),lineCount,&lineCount));
    // Overhang distances are relative to the entire requested layout box, not
    // to text metrics.left/top. A positive top/bottom is actual ink overflow.
    float top=std::max(0.0f,overhang.top),bottom=std::max(0.0f,overhang.bottom);
    bool vertical=top>.01f||bottom>.01f,horizontal=overhang.left>.01f||overhang.right>.01f;
    bool trimmed=std::any_of(lines.begin(),lines.end(),[](auto const& line){return line.isTrimmed!=FALSE;});
    com_ptr<Diagnostics> diagnostics;diagnostics.attach(new Diagnostics(dpi));check_hresult(layout->textLayout->Draw(nullptr,diagnostics.get(),0,0));
    ++audit.samples;audit.verticalOverflow+=vertical?1u:0u;audit.horizontalOverflow+=horizontal?1u:0u;audit.trimmed+=trimmed?1u:0u;
    audit.simulatedRuns+=diagnostics->simulatedRuns;audit.zeroGlyphs+=diagnostics->zeroGlyphs;
    audit.maximumTopOverflow=std::max(audit.maximumTopOverflow,top);audit.maximumBottomOverflow=std::max(audit.maximumBottomOverflow,bottom);
    auto intrinsic=request;intrinsic.width=32768;intrinsic.trim=false;intrinsic.center=false;auto untrimmed=engine.CreateLayout(intrinsic);
    std::ostringstream output;output<<std::fixed<<std::setprecision(6);
    output<<"{\"id\":"<<Json(slot.id)<<",\"group\":"<<Json(slot.group)<<",\"source\":"<<Json(slot.source)
        <<",\"sourceSlotDip\":{\"x\":"<<slot.x<<",\"y\":"<<slot.y<<",\"width\":"<<request.width<<",\"height\":"<<request.height
        <<",\"parentWidth\":"<<slot.shellWidth<<",\"parentHeight\":"<<slot.shellHeight<<"},\"slotPhysicalPixels\":{\"width\":"<<request.width*dpi/96<<",\"height\":"<<request.height*dpi/96
        <<"},\"lineHeightDipRequestedAfterViewClamp\":"<<request.lineHeight<<",\"baselineDipRequestedAfterViewPolicy\":"<<request.baseline<<",\"paragraphCenter\":true,\"horizontalCenter\":"<<(request.center?"true":"false")
        <<",\"wrap\":false,\"trimRequested\":true,\"untrimmedAdvanceDip\":"<<untrimmed->metrics.advance<<",\"lineWasTrimmed\":"<<(trimmed?"true":"false")
        <<",\"metricsTopDip\":"<<metrics.top<<",\"metricsLeftDip\":"<<metrics.left<<",\"layoutWidthDip\":"<<metrics.layoutWidth<<",\"layoutHeightDip\":"<<metrics.layoutHeight
        <<",\"inkBoundsFromOverhangDip\":{\"left\":"<<-overhang.left<<",\"top\":"<<-overhang.top<<",\"right\":"<<request.width+overhang.right<<",\"bottom\":"<<request.height+overhang.bottom
        <<"},\"verticalInkOutsideSlot\":"<<(vertical?"true":"false")<<",\"horizontalInkOutsideSlot\":"<<(horizontal?"true":"false")<<",\"lines\":[";
    for(size_t i=0;i<lines.size();++i)
    {if(i)output<<',';auto const& line=lines[i];output<<"{\"lengthUtf16\":"<<line.length<<",\"trailingWhitespaceLength\":"<<line.trailingWhitespaceLength<<",\"heightDip\":"<<line.height<<",\"baselineDip\":"<<line.baseline<<",\"isTrimmed\":"<<(line.isTrimmed?"true":"false")<<'}';}
    output<<"],\"typography\":"<<Describe(engine,request,slot.id)<<'}';return output.str();
}
class Rasterizer
{
    com_ptr<IWICImagingFactory> wic;
    com_ptr<ID2D1Factory> d2d;
public:
    Rasterizer()
    {check_hresult(CoCreateInstance(CLSID_WICImagingFactory2,nullptr,CLSCTX_INPROC_SERVER,__uuidof(IWICImagingFactory),wic.put_void()));check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,d2d.put()));}
    void Sheet(Engine& engine,fs::path const& path,std::vector<SheetRow> const& rows,float dpi,std::wstring const& title)
    {
        constexpr float width=1180,margin=30;float height=86;for(auto const& row:rows)height+=std::max(54.0f,row.request.fontSize+38);
        UINT physicalWidth=UINT(std::ceil(width*dpi/96)),physicalHeight=UINT(std::ceil(height*dpi/96));
        com_ptr<IWICBitmap> bitmap;check_hresult(wic->CreateBitmap(physicalWidth,physicalHeight,GUID_WICPixelFormat32bppPBGRA,WICBitmapCacheOnLoad,bitmap.put()));
        com_ptr<ID2D1RenderTarget> target;
        auto properties=D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_SOFTWARE,D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),dpi,dpi);
        check_hresult(d2d->CreateWicBitmapRenderTarget(bitmap.get(),properties,target.put()));target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        com_ptr<ID2D1SolidColorBrush> ink,muted,guide;check_hresult(target->CreateSolidColorBrush(D2D1::ColorF(0x20252c),ink.put()));check_hresult(target->CreateSolidColorBrush(D2D1::ColorF(0x626c78),muted.put()));check_hresult(target->CreateSolidColorBrush(D2D1::ColorF(0xcfd5dd),guide.put()));
        com_ptr<IDWriteTextFormat> labels;check_hresult(engine.Factory()->CreateTextFormat(L"Segoe UI",nullptr,DWRITE_FONT_WEIGHT_NORMAL,DWRITE_FONT_STYLE_NORMAL,DWRITE_FONT_STRETCH_NORMAL,12,L"en-US",labels.put()));
        target->BeginDraw();target->Clear(D2D1::ColorF(0xf4f5f7));
        target->DrawText(title.c_str(),UINT32(title.size()),labels.get(),D2D1::RectF(margin,18,width-margin,42),muted.get());
        std::wstring subtitle=L"DirectWrite → WIC/D2D grayscale | native DPI rendering | baseline guides are QA only";
        target->DrawText(subtitle.c_str(),UINT32(subtitle.size()),labels.get(),D2D1::RectF(margin,42,width-margin,66),muted.get());
        float y=80;
        for(auto const& row:rows)
        {
            target->DrawText(row.label.c_str(),UINT32(row.label.size()),labels.get(),D2D1::RectF(margin,y,width-margin,y+18),muted.get());
            auto request=row.request;request.dpi=dpi;auto layout=engine.CreateLayout(request);
            float baseline=y+20+layout->metrics.baseline;target->DrawLine({margin,baseline},{width-margin,baseline},guide.get(),.5f);
            target->DrawTextLayout({margin,y+20},layout->textLayout.get(),ink.get(),D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT);
            y+=std::max(54.0f,request.fontSize+38);
        }
        check_hresult(target->EndDraw());
        com_ptr<IWICStream> stream;check_hresult(wic->CreateStream(stream.put()));check_hresult(stream->InitializeFromFilename(path.c_str(),GENERIC_WRITE));
        com_ptr<IWICBitmapEncoder> encoder;check_hresult(wic->CreateEncoder(GUID_ContainerFormatPng,nullptr,encoder.put()));check_hresult(encoder->Initialize(stream.get(),WICBitmapEncoderNoCache));
        com_ptr<IWICBitmapFrameEncode> frame;check_hresult(encoder->CreateNewFrame(frame.put(),nullptr));check_hresult(frame->Initialize(nullptr));check_hresult(frame->SetSize(physicalWidth,physicalHeight));check_hresult(frame->SetResolution(dpi,dpi));
        WICPixelFormatGUID pixel=GUID_WICPixelFormat32bppPBGRA;check_hresult(frame->SetPixelFormat(&pixel));check_hresult(frame->WriteSource(bitmap.get(),nullptr));check_hresult(frame->Commit());check_hresult(encoder->Commit());
    }
    void FixedSlots(Engine& engine,fs::path const& path,std::vector<FixedSlot> const& slots,float dpi,std::wstring const& group)
    {
        constexpr float width=760,margin=24,padding=24,rowGap=112;size_t count=0;
        for(auto const& slot:slots)if(slot.group==group)++count;
        float height=94+float(count)*rowGap;UINT physicalWidth=UINT(std::ceil(width*dpi/96)),physicalHeight=UINT(std::ceil(height*dpi/96));
        com_ptr<IWICBitmap> bitmap;check_hresult(wic->CreateBitmap(physicalWidth,physicalHeight,GUID_WICPixelFormat32bppPBGRA,WICBitmapCacheOnLoad,bitmap.put()));
        com_ptr<ID2D1RenderTarget> target;auto properties=D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_SOFTWARE,D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),dpi,dpi);
        check_hresult(d2d->CreateWicBitmapRenderTarget(bitmap.get(),properties,target.put()));target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        com_ptr<ID2D1SolidColorBrush> ink,labelInk,guide,overflow;
        check_hresult(target->CreateSolidColorBrush(D2D1::ColorF(0x20252c),ink.put()));check_hresult(target->CreateSolidColorBrush(D2D1::ColorF(0x626c78),labelInk.put()));
        check_hresult(target->CreateSolidColorBrush(D2D1::ColorF(0x1689f9),guide.put()));check_hresult(target->CreateSolidColorBrush(D2D1::ColorF(0xc65c00),overflow.put()));
        com_ptr<IDWriteTextFormat> labels;check_hresult(engine.Factory()->CreateTextFormat(L"Segoe UI",nullptr,DWRITE_FONT_WEIGHT_NORMAL,DWRITE_FONT_STYLE_NORMAL,DWRITE_FONT_STRETCH_NORMAL,11,L"en-US",labels.put()));
        target->BeginDraw();target->Clear(D2D1::ColorF(0xf4f5f7));
        std::wstring title=L"SystemA | fixed player slots | "+group+L" | "+std::to_wstring(int(dpi))+L" DPI";
        std::wstring subtitle=L"Blue: exact view text slot; orange: ink bounds; no clip. Padding/guides are QA only.";
        target->DrawText(title.c_str(),UINT32(title.size()),labels.get(),D2D1::RectF(margin,16,width-margin,36),labelInk.get());
        target->DrawText(subtitle.c_str(),UINT32(subtitle.size()),labels.get(),D2D1::RectF(margin,40,width-margin,62),labelInk.get());
        float rowY=82;
        for(auto const& slot:slots)
        {
            if(slot.group!=group)continue;auto request=slot.request;request.dpi=dpi;auto layout=engine.CreateLayout(request);
            std::wstring label=slot.id+L" | "+std::to_wstring(int(request.width))+L"x"+std::to_wstring(int(request.height))+L" DIP | "+std::to_wstring(int(request.fontSize))+L" / line "+std::to_wstring(int(request.lineHeight));
            target->DrawText(label.c_str(),UINT32(label.size()),labels.get(),D2D1::RectF(margin,rowY,width-margin,rowY+18),labelInk.get());
            float x=margin+padding,y=rowY+42;DWRITE_OVERHANG_METRICS overhang{};check_hresult(layout->textLayout->GetOverhangMetrics(&overhang));
            // Deliberately omit D2D1_DRAW_TEXT_OPTIONS_CLIP and PushAxisAlignedClip.
            target->DrawTextLayout({x,y},layout->textLayout.get(),ink.get(),D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT);
            target->DrawRectangle(D2D1::RectF(x,y,x+request.width,y+request.height),guide.get(),.5f);
            target->DrawLine({x,y+layout->metrics.baseline},{x+request.width,y+layout->metrics.baseline},guide.get(),.35f);
            auto bounds=D2D1::RectF(x-overhang.left,y-overhang.top,x+request.width+overhang.right,y+request.height+overhang.bottom);
            if(bounds.right>bounds.left&&bounds.bottom>bounds.top)target->DrawRectangle(bounds,overflow.get(),.35f);
            rowY+=rowGap;
        }
        check_hresult(target->EndDraw());com_ptr<IWICStream> stream;check_hresult(wic->CreateStream(stream.put()));check_hresult(stream->InitializeFromFilename(path.c_str(),GENERIC_WRITE));
        com_ptr<IWICBitmapEncoder> encoder;check_hresult(wic->CreateEncoder(GUID_ContainerFormatPng,nullptr,encoder.put()));check_hresult(encoder->Initialize(stream.get(),WICBitmapEncoderNoCache));
        com_ptr<IWICBitmapFrameEncode> frame;check_hresult(encoder->CreateNewFrame(frame.put(),nullptr));check_hresult(frame->Initialize(nullptr));check_hresult(frame->SetSize(physicalWidth,physicalHeight));check_hresult(frame->SetResolution(dpi,dpi));
        WICPixelFormatGUID pixel=GUID_WICPixelFormat32bppPBGRA;check_hresult(frame->SetPixelFormat(&pixel));check_hresult(frame->WriteSource(bitmap.get(),nullptr));check_hresult(frame->Commit());check_hresult(encoder->Commit());
    }
};
void FixedPlayerStudy(fs::path const& out,std::vector<float> const& dpis)
{
    Engine engine;engine.Configure(Profile::SystemA,1);Rasterizer rasterizer;auto slots=FixedPlayerSlots();
    std::ostringstream index;index<<"{\"schema\":1,\"mode\":\"fixed-player-slots\",\"profile\":\"system-a\",\"tokenRevision\":1,\"fontRegistration\":false,\"runtimeFontBundling\":false,\"sourceGeometry\":\"WidgetScene.UpdateMetadata/DrawCapsule/DrawPeek/UpdateTimeline + SurfaceOp.Typography line-height clamp\",\"physicalPlayerDpiEvidence\":false,\"xamlDpiEvidence\":false,\"dpiResults\":[";bool first=true;
    for(float dpi:dpis)
    {
        auto stem="system-a-fixed-player-"+std::to_string(int(dpi))+"dpi";FixedSlotAudit audit;
        std::ostringstream report;report<<"{\"schema\":1,\"mode\":\"fixed-player-slots\",\"profile\":\"system-a\",\"dpi\":"<<dpi<<",\"slotSamples\":[";bool firstSlot=true;
        for(auto const& slot:slots){if(!firstSlot)report<<',';firstSlot=false;report<<DescribeFixedSlot(engine,slot,dpi,audit);}
        report<<"],\"audit\":{\"sampleCount\":"<<audit.samples<<",\"trimmedSamples\":"<<audit.trimmed<<",\"verticalInkOverflowSamples\":"<<audit.verticalOverflow<<",\"horizontalInkOverflowSamples\":"<<audit.horizontalOverflow<<",\"maximumTopOverflowDip\":"<<audit.maximumTopOverflow<<",\"maximumBottomOverflowDip\":"<<audit.maximumBottomOverflow<<",\"simulatedRuns\":"<<audit.simulatedRuns<<",\"zeroGlyphsForClusterReview\":"<<audit.zeroGlyphs<<",\"overflowToleranceDip\":0.01,\"automatedWholePlayerPassClaim\":false}}";Save(out/(stem+".json"),report.str());
        if(!first)index<<',';first=false;index<<"{\"dpi\":"<<dpi<<",\"resolutionJson\":"<<Json(stem+".json")<<",\"pngs\":[";bool firstGroup=true;
        for(auto group:{L"capsule",L"peek",L"details",L"clock-numeric"})
        {
            auto png="system-a-fixed-player-"+Utf8(group)+"-"+std::to_string(int(dpi))+"dpi.png";rasterizer.FixedSlots(engine,out/png,slots,dpi,group);
            if(!firstGroup)index<<',';firstGroup=false;index<<Json(png);
        }
        index<<"],\"samples\":"<<audit.samples<<",\"verticalInkOverflowSamples\":"<<audit.verticalOverflow<<",\"simulatedRuns\":"<<audit.simulatedRuns<<",\"requiresVisualAndClusterReview\":true}";
    }
    auto stats=engine.Stats();if(stats.formats>64||stats.layouts>128)throw std::runtime_error("Fixed-slot cache bound violated.");
    index<<"],\"cache\":{\"formats\":"<<stats.formats<<",\"layouts\":"<<stats.layouts<<",\"formatLimit\":64,\"layoutLimit\":128}}";Save(out/"fixed-player-slots-index.json",index.str());
    Save(out/"METHOD.md","# P2 native fixed player slot evidence\n\nRun `GlassDesk.TypographyProbe.exe --fixed-player-slots --out NEW_FONT_STUDY_CHILD [--dpi 96,144,192]`. This mode uses the installed SystemA profile and no private font; it does not install, register or bundle fonts, change system DPI, create a player HWND or run XAML. Each requested DPI is rendered independently through shared TypographyEngine + WIC/D2D grayscale. PNGs are not resized. Existing P1 mode is a separate unchanged path.\n\nGeometry mirrors WidgetScene metadata: capsule text width is shell208/296 minus60 minus52/12 (audio), giving96/136/184/224 DIP; title14/20 DIP, artist12/16 DIP. Peek slots164/200 wide use title18/26 and artist13/20 (catalog line18). Details width304 uses title20 in24 high and line-height28 explicitly clamped24 by SurfaceOp.Typography; TypographyCatalog.BaselineForSlot supplies explicit baseline18.75 only for role9/height24. The P1 token baseline/default line28 stay unchanged. Artist14/20, both centered. Source positions are recorded, but rows are deliberately separated with padded QA guides. The images are not full player layouts.\n\nClock-only capsule25DIP text uses available128/168 width, heights27/32 and clamped line27/32; whiteplate16DIP text is272x26, hero34DIP is192x44. Secondary dates preserve10DIP height16/12 and caption12DIP height21. Numeric timeline captions12DIP use112x18, line16, explicit TabularNumbers; position is leading, duration centered, matching current implementation. Variable-length durations and percentages are never asserted equal width.\n\nChinese, Japanese, Latin, mixed scripts, emoji/ZWJ, Arabic, Hebrew and long ellipsis titles are tested in every metadata slot. DWrite line trim flags, actual glyph-run faces, weights, axes, simulations, clusters, advance, baseline, GetMetrics and GetOverhangMetrics are saved. Untrimmed advance is measured separately under the same role/weight/line constraints at32768 DIP. Positive overhang top/bottom means ink outside the requested slot; tolerance0.01 DIP is reported as a diagnostic flag, not a release pass. Raw zero glyphs still require cluster review for ignorable controls.\n\nBlue outlines show exact text boxes and baselines; orange outlines show GetOverhangMetrics ink bounds. No clip option or axis clip is applied, so overflow remains visible in the24DIP QA padding. All guides/padding/labels are probe-only. Native fixed slots cannot prove actual XAML100/200 rendering, mixed physical player DPI, click targets, foreground contrast, animation, full player overlap or final release acceptance.\n");
}
std::string NumericStudy(Engine& engine,float dpi)
{
    std::ostringstream output;output<<std::fixed<<std::setprecision(6)<<'[';bool first=true;
    for(uint32_t role:{1u,2u,4u,19u,20u,21u})
    {
        auto token=TokenForRole(role);Request request;request.role=role;request.fontSize=token.size;request.lineHeight=token.lineHeight;request.fontWeight=token.latinWeight;request.flags=token.flags;request.locale=L"en-US";request.dpi=dpi;request.width=1000;request.height=100;request.trim=false;request.paragraphCenter=false;
        float minimum=std::numeric_limits<float>::max(),maximum=0;std::wstring minimumText,maximumText;std::vector<float> widths;
        for(int hour=0;hour<24;++hour)for(int minute=0;minute<60;++minute)
        {
            wchar_t text[6]{};swprintf_s(text,L"%02d:%02d",hour,minute);request.text=text;float width=engine.CreateLayout(request)->metrics.advance;widths.push_back(width);
            if(width<minimum){minimum=width;minimumText=text;}if(width>maximum){maximum=width;maximumText=text;}
        }
        if(!first)output<<',';first=false;
        output<<"{\"role\":"<<role<<",\"sizeDip\":"<<token.size<<",\"dpi\":"<<dpi<<",\"flagsRequested\":"<<token.flags<<",\"format\":\"HH:mm\",\"combinations\":1440,\"minimumAdvanceDip\":"<<minimum<<",\"maximumAdvanceDip\":"<<maximum<<",\"deltaDip\":"<<maximum-minimum<<",\"targetMaxDeltaDip\":0.05,\"withinTarget\":"<<(maximum-minimum<=.05f?"true":"false")<<",\"minimumText\":"<<Json(minimumText)<<",\"maximumText\":"<<Json(maximumText)<<",\"advancesDip\":[";
        for(size_t i=0;i<widths.size();++i){if(i)output<<',';output<<widths[i];}output<<"],\"secondsRepresentatives\":[";
        bool secondsFirst=true;float secondsMinimum=std::numeric_limits<float>::max(),secondsMaximum=0;
        for(auto text:{L"00:00:00",L"08:08:08",L"11:11:11",L"12:34:56",L"22:22:22",L"23:59:59"})
        {if(!secondsFirst)output<<',';secondsFirst=false;request.text=text;auto width=engine.CreateLayout(request)->metrics.advance;secondsMinimum=std::min(secondsMinimum,width);secondsMaximum=std::max(secondsMaximum,width);output<<"{\"text\":"<<Json(std::wstring(text))<<",\"advanceDip\":"<<width<<'}';}
        output<<"],\"secondsMinimumAdvanceDip\":"<<secondsMinimum<<",\"secondsMaximumAdvanceDip\":"<<secondsMaximum<<",\"secondsDeltaDip\":"<<secondsMaximum-secondsMinimum<<",\"secondsWithinTarget\":"<<(secondsMaximum-secondsMinimum<=.05f?"true":"false")<<'}';
    }
    output<<']';return output.str();
}
std::string CacheCheck(Engine& engine,Profile profile)
{
    Sample sample{L"cache",L"GlassDesk Music",L"en-US"};auto request=MakeRequest(sample,20,600);
    auto first=engine.CreateLayout(request);auto before=engine.Stats();auto repeated=engine.CreateLayout(request);auto after=engine.Stats();
    bool shared=first.get()==repeated.get()&&before.layoutsCreated==after.layoutsCreated;
    auto resized=request;resized.width+=1;auto widthLayout=engine.CreateLayout(resized);
    auto redpi=request;redpi.dpi=192;auto dpiLayout=engine.CreateLayout(redpi);
    bool dimensionsDistinct=first.get()!=widthLayout.get()&&first.get()!=dpiLayout.get();
    bool semanticKeys=true;
    if(profile!=Profile::Baseline)
    {
        auto language=request;language.locale=L"ja-JP";auto localeLayout=engine.CreateLayout(language);
        auto tracked=request;tracked.trackingEm=-.02f;auto trackingLayout=engine.CreateLayout(tracked);
        auto weighted=request;weighted.fontWeight=500;auto weightLayout=engine.CreateLayout(weighted);
        auto numbered=request;numbered.flags=TabularNumbers;auto numericLayout=engine.CreateLayout(numbered);
        semanticKeys=first.get()!=localeLayout.get()&&first.get()!=trackingLayout.get()&&first.get()!=weightLayout.get()&&first.get()!=numericLayout.get();
    }
    if(!shared||!dimensionsDistinct||!semanticKeys)throw std::runtime_error("Shared-layout/cache key check failed.");
    return "{\"sameRequestSharesImmutableLayout\":true,\"sameRequestCreatesNoLayout\":true,\"dimensionsAndDpiDistinct\":true,\"semanticKeysDistinct\":true,\"legacyIgnoresUnsupportedSemanticInputs\":"+std::string(profile==Profile::Baseline?"true":"false")+"}";
}
}

int wmain(int argc,wchar_t** argv)
{
    try
    {
        fs::path out,noto;std::vector<float> dpis{96,192};bool fixedPlayerSlots=false,dpiSpecified=false;
        for(int i=1;i<argc;++i)
        {
            std::wstring option=argv[i];if(option==L"--out"&&i+1<argc)out=argv[++i];
            else if(option==L"--noto"&&i+1<argc)noto=argv[++i];
            else if(option==L"--dpi"&&i+1<argc){dpiSpecified=true;dpis.clear();std::wstringstream values(argv[++i]);std::wstring value;while(std::getline(values,value,L','))dpis.push_back(std::stof(value));}
            else if(option==L"--fixed-player-slots")fixedPlayerSlots=true;
            else throw std::runtime_error("Usage: GlassDesk.TypographyProbe.exe --out NEW_QA_DIRECTORY --noto PRIVATE_NOTO_TTF [--dpi 96,192]; or --fixed-player-slots --out NEW_QA_DIRECTORY [--dpi 96,144,192]");
        }
        if(fixedPlayerSlots&&!dpiSpecified)dpis={96,144,192};
        if(fixedPlayerSlots&&!noto.empty())throw std::runtime_error("--fixed-player-slots uses installed SystemA only; omit --noto.");
        if(out.empty()||(!fixedPlayerSlots&&(noto.empty()||!fs::is_regular_file(noto)))||dpis.empty())throw std::runtime_error("--out and a valid --noto private font path are required for P1; fixed-player-slots only requires --out.");
        for(float dpi:dpis)if(!std::isfinite(dpi)||dpi<48||dpi>384)throw std::runtime_error("DPI must be finite and in 48..384.");
        out=CheckedOutput(out);fs::create_directories(out);winrt::init_apartment(winrt::apartment_type::multi_threaded);
        if(fixedPlayerSlots)
        {
            FixedPlayerStudy(out,dpis);winrt::uninit_apartment();std::wcout<<L"Fixed player slot study written to "<<out.wstring()<<L'\n';return 0;
        }
        {
            Engine engine;Rasterizer rasterizer;auto samples=Samples();std::ostringstream index;index<<"{\"schema\":1,\"tokenRevision\":1,\"engine\":\"DirectWrite shared layout + WIC software D2D grayscale\",\"privateFontPath\":"<<Json(fs::absolute(noto).wstring())<<",\"fontRegistration\":false,\"productDefault\":\"SystemA\",\"profiles\":[";bool firstProfile=true;
            for(auto profile:{Profile::Baseline,Profile::SystemA,Profile::NotoQa})
            {
                engine.Configure(profile,1,noto.wstring());if(!firstProfile)index<<',';firstProfile=false;index<<"{\"profile\":"<<Json(ProfileName(profile))<<",\"privateFamily\":"<<Json(engine.PrivateFamily())<<",\"cacheCheck\":"<<CacheCheck(engine,profile)<<",\"dpiResults\":[";bool firstDpi=true;
                for(float dpi:dpis)
                {
                    if(!firstDpi)index<<',';firstDpi=false;std::string stem=ProfileName(profile)+"-"+std::to_string(int(dpi))+"dpi";
                    std::ostringstream report;report<<"{\"profile\":"<<Json(ProfileName(profile))<<",\"dpi\":"<<dpi<<",\"samples\":[";bool firstRecord=true;
                    for(float size:{10.0f,12.0f,13.0f,14.0f,16.0f,18.0f,20.0f,25.0f,34.0f,36.0f,48.0f})for(uint32_t weight:{400u,500u,600u})for(auto const& sample:samples)
                    {
                        auto request=MakeRequest(sample,size,weight);request.dpi=dpi;if(!firstRecord)report<<',';firstRecord=false;report<<Describe(engine,request,sample.id);
                    }
                    report<<"],\"tokenSamples\":[";bool firstToken=true;std::vector<SheetRow> tokenRows;
                    for(auto const& token:Tokens())
                    {
                        if(token.role==0)continue;Sample sample{L"token",token.flags?L"今天 08:08 · GlassDesk":L"时间 设置 · GlassDesk Music",L"zh-CN",token.flags};
                        Request request=MakeRequest(sample,token.size,token.latinWeight);request.role=token.role;request.cjkWeight=token.cjkWeight;request.lineHeight=token.lineHeight;request.trackingEm=token.trackingEm;request.dpi=dpi;
                        if(!firstToken)report<<',';firstToken=false;report<<Describe(engine,request,std::wstring(token.name,token.name+strlen(token.name)));
                        std::wstring label(token.name,token.name+strlen(token.name));label+=L" · "+std::to_wstring(int(token.size))+L" DIP · request "+std::to_wstring(token.latinWeight)+L" / CJK "+std::to_wstring(token.cjkWeight);tokenRows.push_back({request,label});
                    }
                    report<<"],\"numeric\":"<<NumericStudy(engine,dpi)<<'}';Save(out/(stem+"-resolution.json"),report.str());
                    std::vector<SheetRow> rows;
                    for(auto setting:{std::pair{14.0f,400u},std::pair{20.0f,600u},std::pair{36.0f,400u}})for(auto const& sample:samples)
                    {auto request=MakeRequest(sample,setting.first,setting.second);std::wstring label=sample.id+L" · "+std::to_wstring(int(setting.first))+L" DIP · request "+std::to_wstring(setting.second);rows.push_back({request,label});}
                    auto profileLabel=ProfileName(profile);std::wstring title(profileLabel.begin(),profileLabel.end());title+=L" | "+std::to_wstring(int(dpi))+L" DPI | identical text, constraints and background";
                    rasterizer.Sheet(engine,out/(stem+"-comparison.png"),rows,dpi,title);rasterizer.Sheet(engine,out/(stem+"-tokens.png"),tokenRows,dpi,title+L" | catalog tokens");
                    index<<"{\"dpi\":"<<dpi<<",\"comparisonPng\":"<<Json(stem+"-comparison.png")<<",\"tokensPng\":"<<Json(stem+"-tokens.png")<<",\"resolutionJson\":"<<Json(stem+"-resolution.json")<<'}';
                }
                auto stats=engine.Stats();if(stats.formats>64||stats.layouts>128)throw std::runtime_error("Cache bound violated.");
                index<<"],\"cache\":{\"formats\":"<<stats.formats<<",\"formatLimit\":64,\"layouts\":"<<stats.layouts<<",\"layoutLimit\":128,\"formatHits\":"<<stats.formatHits<<",\"layoutHits\":"<<stats.layoutHits<<",\"layoutsCreated\":"<<stats.layoutsCreated<<"}}";
            }
            index<<"]}";Save(out/"font-study-index.json",index.str());
            Save(out/"METHOD.md","# Native typography study\n\nAll PNGs are independently rendered by WIC/D2D at the named DPI. They are not resized screenshots. DIP constraints and background #F4F5F7 are identical across profiles; baseline guides and small labels are QA annotations. Gray-scale antialiasing is used. Color-font drawing is requested where supported by this D2D target.\n\nBaseline preserves the existing fixed Segoe UI Variable Text, bool 400/600 mapping, default fallback, natural line metrics and five-character clock heuristic. SystemA and NotoQa use catalog requests and semantic numeric flags. Actual glyph-run faces, face weights, axis values/ranges, simulations, locale, raw zero-glyph counts, cluster maps, baselines and advances are recorded separately from requests. A zero glyph can represent an ignorable control and requires cluster review before calling it a missing character. An opsz request does not prove an opsz axis; unsupportedAxes and actual per-face axes are authoritative.\n\nThe 11-size × 3-weight × 16-string multilingual JSON matrix is broader than the selected visual sheet. HH:mm metrics enumerate all 1440 combinations for six numeric roles. HH:mm:ss records representative widths. Different-length percentages are not asserted equal-width; their field edge alignment belongs to the view.\n\nPrivate Noto is loaded through an in-process DirectWrite font set, with system fallback retained. No font is installed or registered. Product remains SystemA. Layout and format caches are bounded to 128 and 64 and include profile, revision, locale, role, both requested weights, spacing, size, dimensions, DPI and layout flags; color is deliberately not part of layout.\n\nScript itemization uses IDWriteTextAnalyzer1 and logs ISO script properties. Kana, Hangul and Bopomofo runs receive script-sensitive locale names; Han retains explicit caller locale or the inferred content/user locale. Decimal-digit runs use the platform UnicodeGeneralCategory on whole scalar values, including supplementary digits. The caller must initialize a WinRT apartment; the probe initializes MTA.\n\nNew profiles inspect actual shaped fallback runs before caching. If DirectWrite selected a simulated face, the helper selects a nonsimulated installed family member and records the requested and selected weights in simulationFallbacks, then reshapes the paragraph; unresolved simulations fail layout creation. Baseline remains unchanged for comparison. This is a layout-creation pass, not repeated surface text drawing.\n\nAPI references: [Unicode category](https://learn.microsoft.com/en-us/uwp/api/windows.data.text.unicodecharacters.getgeneralcategory), [Script properties](https://learn.microsoft.com/en-us/windows/win32/api/dwrite_1/ns-dwrite_1-dwrite_script_properties), [TextFormat3](https://learn.microsoft.com/en-us/windows/win32/api/dwrite_3/nn-dwrite_3-idwritetextformat3), [FontFallbackBuilder](https://learn.microsoft.com/en-us/windows/win32/api/dwrite_2/nn-dwrite_2-idwritefontfallbackbuilder), [CharacterSpacing](https://learn.microsoft.com/en-us/windows/win32/api/dwrite_1/nf-dwrite_1-idwritetextlayout1-setcharacterspacing).\n");
        }
        winrt::uninit_apartment();std::wcout<<L"Font study written to "<<out.wstring()<<L'\n';return 0;
    }
    catch(winrt::hresult_error const& error){std::wcerr<<L"Typography probe HRESULT 0x"<<std::hex<<uint32_t(error.code())<<L": "<<error.message().c_str()<<L'\n';return 2;}
    catch(std::exception const& error){std::cerr<<error.what()<<'\n';return 3;}
}
