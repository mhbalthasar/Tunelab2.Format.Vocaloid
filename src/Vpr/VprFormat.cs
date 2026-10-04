using System;
using System.IO;
using TuneLab.Foundation;
using TuneLab.SDK;
using VocaloidFormatSupport.Common;
using VocaloidFormatSupport.Vpr;

namespace VocaloidFormatSupport;

// VPR（VOCALOID5/6）导入 + 导出：导入自动识别容器版本，导出由设置选择 V5/V6。
public sealed class VprFormat : IImportFormat, IExportFormat, IExtensionSettings
{
    const string KeyVersion = "vpr_version";
    const string VersionV6 = "6";
    const string VersionV5 = "5";

    VprVersion mVersion = VprVersion.V6;

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var v6 = new ComboBoxItem(VersionV6, "VOCALOID 6  (6.5)");
        var v5 = new ComboBoxItem(VersionV5, "VOCALOID 5  (5.0)");
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add(("vpr_version", "VPR Version (Export)"),
            ComboBoxConfig.Create().Append(v6).Append(v5).WithDefault(v6));
        return ObjectConfig.Create(props);
    }

    public void ApplySettings(PropertyObject settings)
    {
        mVersion = settings.GetString(KeyVersion, VersionV6) == VersionV5 ? VprVersion.V5 : VprVersion.V6;
    }

    public ProjectInfo Deserialize(Stream stream)
    {
        var zip = VprCodec.OpenRead(stream, out var temp);
        try
        {
            var entry = VprCodec.FindSequence(zip)
                ?? throw new InvalidDataException("VPR: ZIP 容器中缺少 Project/sequence.json。");
            using var es = entry.Open();
            return VocMapper.ToTuneLab(VprReader.Read(es));
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidDataException("VPR: Project/sequence.json 不是合法的 JSON。", ex);
        }
        finally
        {
            zip.Dispose();
            temp?.Dispose();
        }
    }

    public void Serialize(Stream output, ProjectInfo info)
    {
        var voc = VocMapper.FromTuneLab(info);
        VprWriter.Write(output, voc, mVersion);
    }
}
