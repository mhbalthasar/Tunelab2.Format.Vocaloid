using System;
using System.IO;
using TuneLab.Foundation;
using TuneLab.SDK;
using VocaloidFormatSupport.Common;
using VocaloidFormatSupport.VSQ;

namespace VocaloidFormatSupport;

// VSQ（VOCALOID2）导入 + 导出：容器 = SMF Format 1 + 每 voice 轨内嵌 VOCALOID INI。
public sealed class VSQFormat : IImportFormat, IExportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }

        return VocMapper.ToTuneLab(VSQReader.Read(bytes));
    }

    public void Serialize(Stream output, ProjectInfo info)
    {
        var voc = VocMapper.FromTuneLab(info);
        VSQWriter.Write(output, voc);
    }
}
