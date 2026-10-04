using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using VocaloidFormatSupport.Common;
using VocaloidFormatSupport.VSQX;

namespace VocaloidFormatSupport;

// VSQX（VOCALOID3 / VOCALOID4）导入 + 导出：导入按根元素 vsq3/vsq4 自动判别；导出由设置选择版本。
public sealed class VSQXFormat : IImportFormat, IExportFormat, IExtensionSettings
{
    const string KeyVersion = "vsqx_version";
    const string VersionV4 = "4";
    const string VersionV3 = "3";

    bool mV4 = true;

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var v4 = new ComboBoxItem(VersionV4, "VOCALOID 4  (vsq4)");
        var v3 = new ComboBoxItem(VersionV3, "VOCALOID 3  (vsq3)");
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add(("vsqx_version", "VSQX Version (Export)"),
            ComboBoxConfig.Create().Append(v4).Append(v3).WithDefault(v4));
        return ObjectConfig.Create(props);
    }

    public void ApplySettings(PropertyObject settings)
    {
        mV4 = settings.GetString(KeyVersion, VersionV4) != VersionV3;
    }

    public ProjectInfo Deserialize(Stream stream)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(stream, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("VSQX: 不是合法的 XML 文档。", ex);
        }

        var rootName = doc.Root?.Name.LocalName;
        if (rootName != "vsq3" && rootName != "vsq4")
            throw new InvalidDataException($"VSQX: 未知的根元素 <{rootName}>（应为 vsq3 / vsq4）。");

        return VocMapper.ToTuneLab(VSQXReader.Read(doc));
    }

    public void Serialize(Stream output, ProjectInfo info)
    {
        var voc = VocMapper.FromTuneLab(info);
        VSQXWriter.Write(output, voc, mV4);
    }
}
