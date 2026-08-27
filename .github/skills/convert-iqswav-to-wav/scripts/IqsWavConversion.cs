using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public sealed class IqsWavConversionReport
{
    public string Status { get; set; }
    public string InputPath { get; set; }
    public string OutputPath { get; set; }
    public bool ReplacedInput { get; set; }
    public bool OrdinaryWav { get; set; }
    public long OriginalBytes { get; set; }
    public string OriginalSha256 { get; set; }
    public uint SampleRate { get; set; }
    public uint PacketSamples { get; set; }
    public uint PacketDataSize { get; set; }
    public uint TriggerRecordLength { get; set; }
    public int PacketCount { get; set; }
    public ulong DataPayloadBytes { get; set; }
    public ulong ValidDataBytes { get; set; }
    public ulong ValidComplexSamples { get; set; }
    public ulong PaddingDiscarded { get; set; }
    public ulong PrivateAndPaddingBytesRemoved { get; set; }
    public uint FinalPacketValidBytes { get; set; }
    public uint PeakPacketIndex { get; set; }
    public uint PeakSampleIndex { get; set; }
    public float PeakPowerDbm { get; set; }
    public short PeakI { get; set; }
    public short PeakQ { get; set; }
    public double PeakMagnitude { get; set; }
    public double Gain { get; set; }
    public long OutputBytes { get; set; }
    public long DataOffset { get; set; }
    public string OutputSha256 { get; set; }
    public string OutputDataSha256 { get; set; }
    public int OutputMaxAbsComponent { get; set; }
    public double OutputPeakMagnitude { get; set; }
}

internal sealed class IqsWavPacket
{
    public uint Index;
    public uint ValidBytes;
    public uint MaxIndex;
    public float MaxPowerDbm;
}

internal sealed class ParsedIqsWav
{
    public IqsWavConversionReport Report;
    public ulong DataPayloadStart;
    public readonly List<IqsWavPacket> Packets = new List<IqsWavPacket>();
}

public static class IqsWavConversion
{
    private const long ProfOffset = 36;
    private const long MagicOffset = 46;
    private const long ProtocolOffset = 50;
    private const long ProfileLengthOffset = 108;
    private const long ProfileDataOffset = 110;
    private const long TriggerChunkOffset = 400;
    private const long TriggerRecordOffset = 408;
    private const long TriggerChunkLength = 25L * 1024L * 1024L;
    private const long DataChunkOffset = TriggerChunkOffset + TriggerChunkLength;
    private const long DataPayloadOffset = DataChunkOffset + 8;
    private const uint DefaultTriggerRecordLength = 405;

    public static IqsWavConversionReport Analyze(string inputPath)
    {
        ParsedIqsWav parsed = Parse(inputPath);
        parsed.Report.Status = "Analyzed";
        return parsed.Report;
    }

    public static IqsWavConversionReport Convert(
        string inputPath,
        string outputPath,
        bool overwrite)
    {
        ParsedIqsWav parsed = Parse(inputPath);
        IqsWavConversionReport report = parsed.Report;
        string inputFullPath = Path.GetFullPath(inputPath);
        string outputFullPath = Path.GetFullPath(outputPath);
        bool replacesInput = PathsEqual(inputFullPath, outputFullPath);

        if (replacesInput && !overwrite)
        {
            throw new IOException("In-place conversion requires overwrite authorization.");
        }
        if (File.Exists(outputFullPath) && !overwrite)
        {
            throw new IOException("Output already exists: " + outputFullPath);
        }

        string outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (String.IsNullOrEmpty(outputDirectory))
        {
            throw new IOException("Output directory is invalid: " + outputFullPath);
        }
        Directory.CreateDirectory(outputDirectory);

        string tempPath = Path.Combine(
            outputDirectory,
            "." + Path.GetFileName(outputFullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        report.OutputPath = outputFullPath;
        report.ReplacedInput = replacesInput;

        try
        {
            WriteOrdinaryWav(parsed, tempPath);
            ValidateOrdinaryWav(tempPath, report, false);

            if (File.Exists(outputFullPath))
            {
                File.Replace(tempPath, outputFullPath, null);
            }
            else
            {
                File.Move(tempPath, outputFullPath);
            }

            ValidateOrdinaryWav(outputFullPath, report, true);
            report.Status = "Converted";
            return report;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }
    }

    private static ParsedIqsWav Parse(string inputPath)
    {
        string fullPath = Path.GetFullPath(inputPath);
        FileInfo fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists || fileInfo.Length < DataPayloadOffset)
        {
            throw new InvalidDataException(
                "Input is not an IQS-WAV with the fixed protocol header: " + fullPath);
        }

        ParsedIqsWav parsed = new ParsedIqsWav();
        IqsWavConversionReport report = new IqsWavConversionReport();
        report.InputPath = fullPath;
        report.OriginalBytes = fileInfo.Length;
        report.OriginalSha256 = HashFile(fullPath);
        report.DataOffset = 44;
        parsed.Report = report;
        parsed.DataPayloadStart = (ulong)DataPayloadOffset;

        using (FileStream stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            RequireTag(stream, 0, "RIFF");
            RequireTag(stream, 8, "WAVE");
            RequireTag(stream, 12, "fmt ");
            RequireTag(stream, ProfOffset, "prof");
            RequireTag(stream, TriggerChunkOffset, "trig");
            RequireTag(stream, DataChunkOffset, "data");

            byte[] magic = ReadAt(stream, MagicOffset, 4);
            if (magic[0] != 0x8C
                || magic[1] != 0x22
                || magic[2] != 0x52
                || magic[3] != 0x9B)
            {
                throw new InvalidDataException("IQS-WAV private magic is invalid: " + fullPath);
            }
            if (ReadBeUInt16(stream, ProtocolOffset) != 0x0002)
            {
                throw new InvalidDataException(
                    "Only IQS-WAV protocol 0x0002 is supported: " + fullPath);
            }

            uint fmtSize = ReadLeUInt32(stream, 16);
            ushort audioFormat = ReadLeUInt16(stream, 20);
            ushort channels = ReadLeUInt16(stream, 22);
            uint fmtSampleRate = ReadLeUInt32(stream, 24);
            ushort bitsPerSample = ReadLeUInt16(stream, 34);
            uint dataSize = ReadLeUInt32(stream, DataChunkOffset + 4);
            if (fmtSize != 16
                || audioFormat != 1
                || channels != 2
                || bitsPerSample != 16
                || dataSize == 0)
            {
                throw new InvalidDataException(
                    "Only two-channel PCM16 IQS-WAV data is supported: " + fullPath);
            }
            if ((ulong)DataPayloadOffset + dataSize > (ulong)stream.Length)
            {
                throw new InvalidDataException("IQS-WAV data payload is truncated: " + fullPath);
            }

            ushort profileLength = ReadBeUInt16(stream, ProfileLengthOffset);
            if (profileLength == 0)
            {
                throw new InvalidDataException("IQS-WAV profile is empty: " + fullPath);
            }
            byte[] profile = ReadAt(stream, ProfileDataOffset, profileLength);
            List<double> fields = ParseMsgPackNumbers(profile);
            if (fields.Count < 49)
            {
                throw new InvalidDataException(
                    "IQS-WAV profile has fewer than 49 sequential numeric fields: " + fullPath);
            }

            double profileSampleRate = fields[43];
            double packetSamples = fields[47];
            double packetDataSize = fields[48];
            report.SampleRate = profileSampleRate > 0.0
                ? RoundPositiveToUInt32(profileSampleRate)
                : fmtSampleRate;
            report.PacketSamples = RoundPositiveToUInt32(packetSamples);
            report.PacketDataSize = RoundPositiveToUInt32(packetDataSize);
            report.DataPayloadBytes = dataSize;
            if (report.SampleRate == 0
                || report.PacketSamples == 0
                || report.PacketDataSize == 0
                || (report.PacketDataSize % 4) != 0
                || report.PacketSamples > report.PacketDataSize / 4)
            {
                throw new InvalidDataException(
                    "IQS-WAV packet metadata is invalid: " + fullPath);
            }

            byte[] firstTrigger = ReadAt(
                stream, TriggerRecordOffset, checked((int)DefaultTriggerRecordLength));
            ushort triggerInfoLength = BeUInt16(firstTrigger, 0);
            int deviceStateLengthOffset = 2 + triggerInfoLength;
            if (triggerInfoLength == 0 || deviceStateLengthOffset + 2 > firstTrigger.Length)
            {
                throw new InvalidDataException(
                    "IQS-WAV first TriggerRecord is invalid: " + fullPath);
            }
            ushort deviceStateLength = BeUInt16(firstTrigger, deviceStateLengthOffset);
            uint computedTriggerLength =
                2U + triggerInfoLength + 2U + deviceStateLength + 4U + 4U + 4U;
            if (computedTriggerLength < DefaultTriggerRecordLength
                || computedTriggerLength >= TriggerChunkLength)
            {
                throw new InvalidDataException(
                    "IQS-WAV TriggerRecord length is invalid: " + fullPath);
            }
            report.TriggerRecordLength = computedTriggerLength;

            ulong slotCount =
                ((ulong)dataSize + report.PacketDataSize - 1UL) / report.PacketDataSize;
            if (slotCount == 0 || slotCount > Int32.MaxValue)
            {
                throw new InvalidDataException(
                    "IQS-WAV packet count is invalid: " + fullPath);
            }

            ulong validBytesTotal = 0;
            for (ulong packetIndex = 0; packetIndex < slotCount; ++packetIndex)
            {
                long triggerOffset = checked(
                    TriggerRecordOffset + (long)(packetIndex * report.TriggerRecordLength));
                byte[] trigger = ReadAt(
                    stream, triggerOffset, checked((int)report.TriggerRecordLength));
                ushort currentTriggerInfoLength = BeUInt16(trigger, 0);
                int currentDeviceLengthOffset = 2 + currentTriggerInfoLength;
                if (currentTriggerInfoLength == 0
                    || currentDeviceLengthOffset + 2 > trigger.Length)
                {
                    throw new InvalidDataException(
                        "IQS-WAV TriggerRecord header is invalid at packet "
                        + packetIndex.ToString(CultureInfo.InvariantCulture));
                }
                ushort currentDeviceLength = BeUInt16(trigger, currentDeviceLengthOffset);
                uint currentRecordLength =
                    2U + currentTriggerInfoLength + 2U + currentDeviceLength + 4U + 4U + 4U;
                if (currentRecordLength != report.TriggerRecordLength)
                {
                    throw new InvalidDataException(
                        "IQS-WAV TriggerRecord size changes at packet "
                        + packetIndex.ToString(CultureInfo.InvariantCulture));
                }

                ulong slotStart = packetIndex * report.PacketDataSize;
                ulong remaining = (ulong)dataSize - slotStart;
                uint slotBytes = remaining > report.PacketDataSize
                    ? report.PacketDataSize
                    : (uint)remaining;
                ushort recordValidBytes = BeUInt16(trigger, 10);
                if (recordValidBytes == 0 || recordValidBytes > slotBytes)
                {
                    throw new InvalidDataException(
                        "IQS-WAV valid-byte count is invalid at packet "
                        + packetIndex.ToString(CultureInfo.InvariantCulture));
                }
                uint validBytes = (uint)recordValidBytes;
                if ((validBytes % 4) != 0)
                {
                    throw new InvalidDataException(
                        "IQS-WAV valid-byte count is not Complex16 aligned at packet "
                        + packetIndex.ToString(CultureInfo.InvariantCulture));
                }

                IqsWavPacket packet = new IqsWavPacket();
                packet.Index = (uint)packetIndex;
                packet.ValidBytes = validBytes;
                packet.MaxPowerDbm = BeSingle(trigger, 397);
                packet.MaxIndex = BeUInt32(trigger, 401);
                parsed.Packets.Add(packet);
                validBytesTotal += validBytes;
            }

            report.PacketCount = parsed.Packets.Count;
            report.ValidDataBytes = validBytesTotal;
            report.ValidComplexSamples = validBytesTotal / 4;
            report.PaddingDiscarded = (ulong)dataSize - validBytesTotal;
            report.FinalPacketValidBytes =
                parsed.Packets[parsed.Packets.Count - 1].ValidBytes;
            if (validBytesTotal == 0 || validBytesTotal > UInt32.MaxValue)
            {
                throw new InvalidDataException(
                    "IQS-WAV valid payload is empty or exceeds legacy RIFF: " + fullPath);
            }

            IqsWavPacket bestPacket = null;
            float bestPower = Single.NegativeInfinity;
            for (int packetIndex = 0; packetIndex < parsed.Packets.Count; ++packetIndex)
            {
                IqsWavPacket packet = parsed.Packets[packetIndex];
                if (!PacketHasValidPeakIndex(packet)
                    || Single.IsNaN(packet.MaxPowerDbm)
                    || Single.IsInfinity(packet.MaxPowerDbm))
                {
                    continue;
                }
                if (bestPacket == null || packet.MaxPowerDbm > bestPower)
                {
                    bestPacket = packet;
                    bestPower = packet.MaxPowerDbm;
                }
            }

            short peakI = 0;
            short peakQ = 0;
            double peakMagnitude = 0.0;
            if (bestPacket != null)
            {
                ReadComplexSample(stream, parsed, bestPacket, out peakI, out peakQ);
                peakMagnitude = Magnitude(peakI, peakQ);
            }

            if (peakMagnitude <= 0.0)
            {
                for (int packetIndex = 0; packetIndex < parsed.Packets.Count; ++packetIndex)
                {
                    IqsWavPacket packet = parsed.Packets[packetIndex];
                    if (!PacketHasValidPeakIndex(packet))
                    {
                        continue;
                    }
                    short localI;
                    short localQ;
                    ReadComplexSample(stream, parsed, packet, out localI, out localQ);
                    double localMagnitude = Magnitude(localI, localQ);
                    if (localMagnitude > peakMagnitude)
                    {
                        bestPacket = packet;
                        peakI = localI;
                        peakQ = localQ;
                        peakMagnitude = localMagnitude;
                    }
                }
            }

            if (bestPacket == null || peakMagnitude <= 0.0)
            {
                throw new InvalidDataException(
                    "IQS-WAV AutoScale peak metadata is unusable: " + fullPath);
            }
            report.PeakPacketIndex = bestPacket.Index;
            report.PeakSampleIndex = bestPacket.MaxIndex;
            report.PeakPowerDbm = bestPacket.MaxPowerDbm;
            report.PeakI = peakI;
            report.PeakQ = peakQ;
            report.PeakMagnitude = peakMagnitude;
            report.Gain = 32767.0 / peakMagnitude;
        }
        return parsed;
    }

    private static void WriteOrdinaryWav(ParsedIqsWav parsed, string outputPath)
    {
        IqsWavConversionReport report = parsed.Report;
        using (FileStream input = new FileStream(
            report.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (FileStream output = new FileStream(
            outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            WriteAscii(output, "RIFF");
            WriteLeUInt32(output, checked(36U + (uint)report.ValidDataBytes));
            WriteAscii(output, "WAVE");
            WriteAscii(output, "fmt ");
            WriteLeUInt32(output, 16);
            WriteLeUInt16(output, 1);
            WriteLeUInt16(output, 2);
            WriteLeUInt32(output, report.SampleRate);
            WriteLeUInt32(output, checked(report.SampleRate * 4));
            WriteLeUInt16(output, 4);
            WriteLeUInt16(output, 16);
            WriteAscii(output, "data");
            WriteLeUInt32(output, (uint)report.ValidDataBytes);

            for (int packetIndex = 0; packetIndex < parsed.Packets.Count; ++packetIndex)
            {
                IqsWavPacket packet = parsed.Packets[packetIndex];
                long sourceOffset = checked((long)(
                    parsed.DataPayloadStart
                    + (ulong)packet.Index * report.PacketDataSize));
                byte[] bytes = ReadAt(
                    input, sourceOffset, checked((int)packet.ValidBytes));
                ScaleLittleEndianPcm16(bytes, report.Gain);
                output.Write(bytes, 0, bytes.Length);
            }
            output.Flush(true);
        }
    }

    private static void ValidateOrdinaryWav(
        string path,
        IqsWavConversionReport report,
        bool finalPath)
    {
        FileInfo fileInfo = new FileInfo(path);
        long expectedSize = checked(44L + (long)report.ValidDataBytes);
        if (!fileInfo.Exists || fileInfo.Length != expectedSize)
        {
            throw new InvalidDataException(
                "Converted ordinary WAV file size is invalid: " + path);
        }

        int maxAbsComponent = 0;
        double peakMagnitude = 0.0;
        using (FileStream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            RequireTag(stream, 0, "RIFF");
            RequireTag(stream, 8, "WAVE");
            RequireTag(stream, 12, "fmt ");
            RequireTag(stream, 36, "data");
            if (ReadLeUInt32(stream, 4) != fileInfo.Length - 8
                || ReadLeUInt32(stream, 16) != 16
                || ReadLeUInt16(stream, 20) != 1
                || ReadLeUInt16(stream, 22) != 2
                || ReadLeUInt32(stream, 24) != report.SampleRate
                || ReadLeUInt32(stream, 28) != checked(report.SampleRate * 4)
                || ReadLeUInt16(stream, 32) != 4
                || ReadLeUInt16(stream, 34) != 16
                || ReadLeUInt32(stream, 40) != report.ValidDataBytes)
            {
                throw new InvalidDataException(
                    "Converted ordinary WAV header is invalid: " + path);
            }

            byte[] buffer = new byte[1024 * 1024];
            ulong remaining = report.ValidDataBytes;
            while (remaining > 0)
            {
                int count = (int)Math.Min((ulong)buffer.Length, remaining);
                int read = stream.Read(buffer, 0, count);
                if (read != count || (read % 4) != 0)
                {
                    throw new EndOfStreamException(
                        "Converted ordinary WAV payload is truncated: " + path);
                }
                for (int offset = 0; offset < read; offset += 4)
                {
                    short iValue = LeInt16(buffer, offset);
                    short qValue = LeInt16(buffer, offset + 2);
                    maxAbsComponent = Math.Max(
                        maxAbsComponent, Math.Abs((int)iValue));
                    maxAbsComponent = Math.Max(
                        maxAbsComponent, Math.Abs((int)qValue));
                    peakMagnitude = Math.Max(
                        peakMagnitude, Magnitude(iValue, qValue));
                }
                remaining -= (ulong)read;
            }
        }

        string dataHash = HashRange(path, 44, report.ValidDataBytes);
        if (!String.IsNullOrEmpty(report.OutputDataSha256)
            && !String.Equals(
                report.OutputDataSha256,
                dataHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Converted ordinary WAV payload hash changed during commit: " + path);
        }
        if (peakMagnitude > 32767.000001)
        {
            throw new InvalidDataException(String.Format(
                CultureInfo.InvariantCulture,
                "Converted ordinary WAV exceeds the Streaming complex peak: {0:R}",
                peakMagnitude));
        }

        report.OrdinaryWav = true;
        report.OutputBytes = fileInfo.Length;
        report.OutputSha256 = HashFile(path);
        report.OutputDataSha256 = dataHash;
        report.OutputMaxAbsComponent = maxAbsComponent;
        report.OutputPeakMagnitude = peakMagnitude;
        report.PrivateAndPaddingBytesRemoved =
            report.OriginalBytes >= fileInfo.Length
                ? (ulong)(report.OriginalBytes - fileInfo.Length)
                : 0;
        if (finalPath)
        {
            report.OutputPath = Path.GetFullPath(path);
        }
    }

    private static List<double> ParseMsgPackNumbers(byte[] data)
    {
        List<double> fields = new List<double>(49);
        int offset = 0;
        double value;
        while (offset < data.Length
            && TryParseMsgPackNumber(data, ref offset, out value))
        {
            fields.Add(value);
        }
        return fields;
    }

    private static bool TryParseMsgPackNumber(
        byte[] data,
        ref int offset,
        out double value)
    {
        value = 0.0;
        if (offset >= data.Length)
        {
            return false;
        }
        byte code = data[offset++];
        if (code <= 0x7F)
        {
            value = code;
            return true;
        }
        if (code >= 0xE0)
        {
            value = unchecked((sbyte)code);
            return true;
        }

        switch (code)
        {
            case 0xC0:
            case 0xC2:
                value = 0.0;
                return true;
            case 0xC3:
                value = 1.0;
                return true;
            case 0xCC:
                if (!HasBytes(data, offset, 1)) return false;
                value = data[offset++];
                return true;
            case 0xCD:
                if (!HasBytes(data, offset, 2)) return false;
                value = BeUInt16(data, offset);
                offset += 2;
                return true;
            case 0xCE:
                if (!HasBytes(data, offset, 4)) return false;
                value = BeUInt32(data, offset);
                offset += 4;
                return true;
            case 0xCF:
                if (!HasBytes(data, offset, 8)) return false;
                value = BeUInt64(data, offset);
                offset += 8;
                return true;
            case 0xD0:
                if (!HasBytes(data, offset, 1)) return false;
                value = unchecked((sbyte)data[offset++]);
                return true;
            case 0xD1:
                if (!HasBytes(data, offset, 2)) return false;
                value = unchecked((short)BeUInt16(data, offset));
                offset += 2;
                return true;
            case 0xD2:
                if (!HasBytes(data, offset, 4)) return false;
                value = unchecked((int)BeUInt32(data, offset));
                offset += 4;
                return true;
            case 0xD3:
                if (!HasBytes(data, offset, 8)) return false;
                value = unchecked((long)BeUInt64(data, offset));
                offset += 8;
                return true;
            case 0xCA:
                if (!HasBytes(data, offset, 4)) return false;
                value = UInt32BitsToSingle(BeUInt32(data, offset));
                offset += 4;
                return true;
            case 0xCB:
                if (!HasBytes(data, offset, 8)) return false;
                value = UInt64BitsToDouble(BeUInt64(data, offset));
                offset += 8;
                return true;
            default:
                return false;
        }
    }

    private static bool PacketHasValidPeakIndex(IqsWavPacket packet)
    {
        return packet.ValidBytes >= 4
            && (ulong)packet.MaxIndex * 4UL + 4UL <= packet.ValidBytes;
    }

    private static void ReadComplexSample(
        FileStream stream,
        ParsedIqsWav parsed,
        IqsWavPacket packet,
        out short iValue,
        out short qValue)
    {
        long offset = checked((long)(
            parsed.DataPayloadStart
            + (ulong)packet.Index * parsed.Report.PacketDataSize
            + (ulong)packet.MaxIndex * 4UL));
        byte[] sample = ReadAt(stream, offset, 4);
        iValue = LeInt16(sample, 0);
        qValue = LeInt16(sample, 2);
    }

    private static void ScaleLittleEndianPcm16(byte[] bytes, double gain)
    {
        for (int offset = 0; offset < bytes.Length; offset += 2)
        {
            short value = LeInt16(bytes, offset);
            double scaled = value * gain;
            short output;
            if (scaled > Int16.MaxValue)
            {
                output = Int16.MaxValue;
            }
            else if (scaled < Int16.MinValue)
            {
                output = Int16.MinValue;
            }
            else
            {
                output = (short)scaled;
            }
            ushort bits = unchecked((ushort)output);
            bytes[offset] = (byte)(bits & 0xFF);
            bytes[offset + 1] = (byte)(bits >> 8);
        }
    }

    private static double Magnitude(short iValue, short qValue)
    {
        double i = iValue;
        double q = qValue;
        return Math.Sqrt(i * i + q * q);
    }

    private static short LeInt16(byte[] data, int offset)
    {
        return unchecked((short)(
            data[offset] | (data[offset + 1] << 8)));
    }

    private static uint RoundPositiveToUInt32(double value)
    {
        if (!(value > 0.0) || value > UInt32.MaxValue)
        {
            return 0;
        }
        return checked((uint)Math.Floor(value + 0.5));
    }

    private static bool HasBytes(byte[] data, int offset, int count)
    {
        return offset >= 0
            && count >= 0
            && offset <= data.Length - count;
    }

    private static ushort BeUInt16(byte[] data, int offset)
    {
        return (ushort)(
            (data[offset] << 8) | data[offset + 1]);
    }

    private static uint BeUInt32(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24)
            | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8)
            | data[offset + 3];
    }

    private static ulong BeUInt64(byte[] data, int offset)
    {
        return ((ulong)BeUInt32(data, offset) << 32)
            | BeUInt32(data, offset + 4);
    }

    private static float BeSingle(byte[] data, int offset)
    {
        return UInt32BitsToSingle(BeUInt32(data, offset));
    }

    private static float UInt32BitsToSingle(uint bits)
    {
        return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
    }

    private static double UInt64BitsToDouble(ulong bits)
    {
        return BitConverter.ToDouble(BitConverter.GetBytes(bits), 0);
    }

    private static byte[] ReadAt(FileStream stream, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset > stream.Length - count)
        {
            throw new EndOfStreamException();
        }
        byte[] data = new byte[count];
        stream.Position = offset;
        int completed = 0;
        while (completed < count)
        {
            int read = stream.Read(data, completed, count - completed);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }
            completed += read;
        }
        return data;
    }

    private static ushort ReadLeUInt16(FileStream stream, long offset)
    {
        byte[] data = ReadAt(stream, offset, 2);
        return (ushort)(data[0] | (data[1] << 8));
    }

    private static uint ReadLeUInt32(FileStream stream, long offset)
    {
        byte[] data = ReadAt(stream, offset, 4);
        return (uint)data[0]
            | ((uint)data[1] << 8)
            | ((uint)data[2] << 16)
            | ((uint)data[3] << 24);
    }

    private static ushort ReadBeUInt16(FileStream stream, long offset)
    {
        return BeUInt16(ReadAt(stream, offset, 2), 0);
    }

    private static void RequireTag(
        FileStream stream,
        long offset,
        string expected)
    {
        string actual = Encoding.ASCII.GetString(
            ReadAt(stream, offset, 4));
        if (!String.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(String.Format(
                CultureInfo.InvariantCulture,
                "Expected chunk {0} at offset {1}, found {2}.",
                expected,
                offset,
                actual));
        }
    }

    private static void WriteAscii(FileStream stream, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteLeUInt16(FileStream stream, ushort value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)(value >> 8));
    }

    private static void WriteLeUInt32(FileStream stream, uint value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)(value >> 24));
    }

    private static bool PathsEqual(string first, string second)
    {
        StringComparison comparison =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        return String.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            comparison);
    }

    private static string HashFile(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            return ToHex(sha.ComputeHash(stream));
        }
    }

    private static string HashRange(
        string path,
        long offset,
        ulong length)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            stream.Position = offset;
            byte[] buffer = new byte[1024 * 1024];
            ulong remaining = length;
            while (remaining > 0)
            {
                int count = (int)Math.Min((ulong)buffer.Length, remaining);
                int read = stream.Read(buffer, 0, count);
                if (read != count)
                {
                    throw new EndOfStreamException();
                }
                sha.TransformBlock(buffer, 0, read, null, 0);
                remaining -= (ulong)read;
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return ToHex(sha.Hash);
        }
    }

    private static string ToHex(byte[] bytes)
    {
        StringBuilder builder = new StringBuilder(bytes.Length * 2);
        for (int index = 0; index < bytes.Length; ++index)
        {
            builder.Append(
                bytes[index].ToString("X2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}

