using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Previs;

/// <summary>A strict BVH reader. Channel order is retained; no engine importer or executable data is used.</summary>
public sealed class BvhMotion
{
    public sealed class Joint
    {
        public string Name { get; internal set; } = "";
        public int Parent { get; internal set; } = -1;
        public Vector3 Offset { get; internal set; }
        public bool IsEndSite { get; internal set; }
        public int ChannelOffset { get; internal set; }
        public string[] Channels { get; internal set; } = Array.Empty<string>();
        public List<int> Children { get; } = new();
    }

    public IReadOnlyList<Joint> Joints => _joints;
    public int FrameCount { get; private set; }
    public int ChannelCount { get; private set; }
    public double FrameTime { get; private set; }
    public double DurationSeconds => FrameCount * FrameTime;
    public double SourceFps => 1.0 / FrameTime;
    public int AnimatedJointCount { get; private set; }
    public bool HasNonRootTranslation { get; private set; }
    private readonly List<Joint> _joints = new();
    private float[] _samples = Array.Empty<float>();

    public static BvhMotion FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new FormatException("Choose a BVH motion file.");
        try
        {
            var file = new FileInfo(path);
            if (file.Length > 256L * 1024 * 1024)
                throw new FormatException("This BVH file exceeds the prototype's 256 MB import limit.");
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            throw new FormatException($"Could not read BVH file: {ex.Message}", ex);
        }
    }

    public static BvhMotion Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The BVH file is empty.");
        var reader = new Reader(text);
        var clip = new BvhMotion();
        reader.Expect("HIERARCHY");
        reader.Expect("ROOT");
        clip.ReadJoint(reader, -1, false);
        reader.Expect("MOTION");
        reader.Expect("Frames");
        reader.SkipOptionalColon();
        clip.FrameCount = reader.PositiveInt("frame count", 1_000_000);
        reader.Expect("Frame");
        reader.Expect("Time");
        reader.SkipOptionalColon();
        clip.FrameTime = reader.Number("frame time");
        if (clip.FrameTime <= 0 || clip.FrameTime > 60)
            throw reader.Error("Frame time must be greater than zero and no more than 60 seconds.");
        if (clip.ChannelCount == 0) throw reader.Error("The skeleton has no motion channels.");
        long sampleCount = (long)clip.FrameCount * clip.ChannelCount;
        if (sampleCount > 32_000_000)
            throw reader.Error("This motion exceeds the prototype's 32 million channel-value import limit.");
        clip._samples = new float[(int)sampleCount];
        for (int i = 0; i < clip._samples.Length; i++)
        {
            if (reader.AtEnd)
                throw reader.Error($"Motion data ended early at frame {i / clip.ChannelCount + 1}; expected {clip.FrameCount} frames with {clip.ChannelCount} values each.");
            double value = reader.Number($"motion value in frame {i / clip.ChannelCount + 1}");
            if (Math.Abs(value) > 1e8) throw reader.Error("A motion channel value is too large.");
            clip._samples[i] = (float)value;
        }
        if (!reader.AtEnd) throw reader.Error("Unexpected values after the declared motion frames. Check the frame count and channel count.");
        return clip;
    }

    private int ReadJoint(Reader reader, int parent, bool endSite)
    {
        if (_joints.Count >= 512) throw reader.Error("The skeleton exceeds the prototype's 512-joint limit.");
        string name = endSite ? _joints[parent].Name + "_EndSite" + _joints.Count : reader.Take("joint name");
        if (name is "{" or "}") throw reader.Error("A joint name is missing.");
        int index = _joints.Count;
        var joint = new Joint { Name = name, Parent = parent, IsEndSite = endSite, ChannelOffset = ChannelCount };
        _joints.Add(joint);
        if (parent >= 0) _joints[parent].Children.Add(index);
        else if (endSite) throw reader.Error("An End Site must have a parent joint.");
        reader.Expect("{");
        reader.Expect("OFFSET");
        joint.Offset = new Vector3((float)reader.Number("X offset"), (float)reader.Number("Y offset"), (float)reader.Number("Z offset"));
        if (!joint.Offset.IsFinite()) throw reader.Error("A skeleton offset is outside the supported numeric range.");
        if (!endSite)
        {
            AnimatedJointCount++;
            reader.Expect("CHANNELS");
            int count = reader.NonNegativeInt("channel count", 6);
            joint.Channels = new string[count];
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < count; c++)
            {
                string channel = reader.Take("channel name");
                string normalized = channel.ToLowerInvariant();
                if (normalized is not ("xposition" or "yposition" or "zposition" or "xrotation" or "yrotation" or "zrotation"))
                    throw reader.Error($"Unsupported channel '{channel}' on joint '{name}'.");
                if (!used.Add(normalized)) throw reader.Error($"Duplicate channel '{channel}' on joint '{name}'.");
                joint.Channels[c] = normalized;
                if (parent >= 0 && normalized.EndsWith("position", StringComparison.Ordinal)) HasNonRootTranslation = true;
            }
            ChannelCount += count;
        }
        while (!reader.Is("}"))
        {
            if (reader.AtEnd) throw reader.Error($"Joint '{name}' has no closing brace.");
            if (reader.Is("JOINT")) { reader.Take("JOINT"); ReadJoint(reader, index, false); }
            else if (reader.Is("End"))
            {
                reader.Take("End");
                reader.Expect("Site");
                ReadJoint(reader, index, true);
            }
            else throw reader.Error($"Expected JOINT, End Site, or a closing brace in '{name}'.");
            if (endSite) throw reader.Error("An End Site cannot contain child joints.");
        }
        reader.Expect("}");
        return index;
    }

    /// <summary>Samples the source hierarchy at absolute time, preserving each joint's intrinsic rotation order.</summary>
    public void Sample(double seconds, bool loop, Quaternion[] globalRotations)
    {
        GetFrameWindow(seconds, loop, out int a, out int b, out float blend, out _);
        for (int j = 0; j < _joints.Count; j++)
        {
            Quaternion rotation = ReadRotation(j, a).Slerp(ReadRotation(j, b), blend).Normalized();
            int parent = _joints[j].Parent;
            globalRotations[j] = parent < 0 ? rotation : (globalRotations[parent] * rotation).Normalized();
        }
    }

    /// <summary>Returns source joint positions at exact source frames, in source units.</summary>
    public Vector3[] BuildRootCurve(int sourceJoint)
    {
        var curve = new Vector3[FrameCount];
        var rotations = new Quaternion[_joints.Count];
        var positions = new Vector3[_joints.Count];
        for (int frame = 0; frame < FrameCount; frame++)
        {
            for (int j = 0; j <= sourceJoint; j++)
            {
                var joint = _joints[j];
                Quaternion localRotation = ReadRotation(j, frame);
                Vector3 localPosition = joint.Offset + ReadTranslation(j, frame);
                rotations[j] = joint.Parent < 0 ? localRotation : (rotations[joint.Parent] * localRotation).Normalized();
                positions[j] = joint.Parent < 0 ? localPosition : positions[joint.Parent] + rotations[joint.Parent] * localPosition;
            }
            curve[frame] = positions[sourceJoint];
        }
        return curve;
    }

    public Vector3 SampleRootCurve(Vector3[] curve, double seconds, bool loop)
    {
        GetFrameWindow(seconds, loop, out int a, out int b, out float blend, out double cycle);
        Vector3 displacement = Vector3.Zero;
        if (curve.Length > 1)
        {
            displacement = curve[^1] - curve[0] + (curve[^1] - curve[^2]);
            displacement.Y = 0; // A repeating clip must not accumulate vertical drift.
        }
        Vector3 end = curve[b];
        if (loop && b == 0 && a != 0) end += displacement;
        return curve[a].Lerp(end, blend) - curve[0] + displacement * (float)cycle;
    }

    public void GetFrameWindow(double seconds, bool loop, out int a, out int b, out float blend, out double cycle)
    {
        if (!double.IsFinite(seconds)) seconds = 0;
        cycle = 0;
        if (loop)
        {
            cycle = Math.Floor(seconds / DurationSeconds);
            seconds -= cycle * DurationSeconds;
        }
        else seconds = Math.Clamp(seconds, 0, Math.Max(0, (FrameCount - 1) * FrameTime));
        double frame = seconds / FrameTime;
        a = Math.Clamp((int)Math.Floor(frame), 0, FrameCount - 1);
        b = loop ? (a + 1) % FrameCount : Math.Min(a + 1, FrameCount - 1);
        blend = (float)Math.Clamp(frame - a, 0, 1);
    }

    private Quaternion ReadRotation(int jointIndex, int frame)
    {
        Joint joint = _joints[jointIndex];
        int start = frame * ChannelCount + joint.ChannelOffset;
        Quaternion rotation = Quaternion.Identity;
        for (int c = 0; c < joint.Channels.Length; c++)
        {
            string channel = joint.Channels[c];
            if (!channel.EndsWith("rotation", StringComparison.Ordinal)) continue;
            float angle = Mathf.DegToRad(_samples[start + c] % 360.0f);
            Vector3 axis = channel[0] switch { 'x' => Vector3.Right, 'y' => Vector3.Up, _ => Vector3.Back };
            rotation = rotation * new Quaternion(axis, angle);
        }
        return rotation.Normalized();
    }

    private Vector3 ReadTranslation(int jointIndex, int frame)
    {
        Joint joint = _joints[jointIndex];
        int start = frame * ChannelCount + joint.ChannelOffset;
        Vector3 position = Vector3.Zero;
        for (int c = 0; c < joint.Channels.Length; c++)
        {
            string channel = joint.Channels[c];
            if (!channel.EndsWith("position", StringComparison.Ordinal)) continue;
            if (channel[0] == 'x') position.X = _samples[start + c];
            if (channel[0] == 'y') position.Y = _samples[start + c];
            if (channel[0] == 'z') position.Z = _samples[start + c];
        }
        return position;
    }

    private sealed class Reader
    {
        private readonly string _text;
        private int _position;
        private int _line = 1;
        private string? _peek;
        public Reader(string text) { _text = text.TrimStart('\uFEFF'); }
        public bool AtEnd => Peek().Length == 0;
        public bool Is(string value) => string.Equals(Peek().TrimEnd(':'), value, StringComparison.OrdinalIgnoreCase);
        public string Take(string description)
        {
            string token = Peek();
            _peek = null;
            if (token.Length == 0) throw Error($"Expected {description}, but the file ended.");
            return token;
        }
        public void Expect(string token)
        {
            string found = Take(token);
            if (!string.Equals(found.TrimEnd(':'), token, StringComparison.OrdinalIgnoreCase))
                throw Error($"Expected '{token}', found '{found}'.");
        }
        public void SkipOptionalColon() { if (Peek() == ":") Take(":"); }
        public double Number(string description)
        {
            string value = Take(description);
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                throw Error($"Invalid {description}: '{value}'. Numbers must use a decimal point.");
            return number;
        }
        public int PositiveInt(string description, int limit)
        {
            int value = NonNegativeInt(description, limit);
            if (value == 0) throw Error($"The {description} must be greater than zero.");
            return value;
        }
        public int NonNegativeInt(string description, int limit)
        {
            string token = Take(description);
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0 || value > limit)
                throw Error($"Invalid {description}: expected a whole number from 0 to {limit}.");
            return value;
        }
        public FormatException Error(string message) => new($"BVH line {_line}: {message}");
        private string Peek()
        {
            if (_peek != null) return _peek;
            while (_position < _text.Length)
            {
                char character = _text[_position];
                if (char.IsWhiteSpace(character)) { if (character == '\n') _line++; _position++; continue; }
                if (character == '#' || (character == '/' && _position + 1 < _text.Length && _text[_position + 1] == '/'))
                {
                    while (_position < _text.Length && _text[_position] != '\n') _position++;
                    continue;
                }
                break;
            }
            if (_position == _text.Length) return _peek = "";
            int start = _position++;
            if (_text[start] is '{' or '}') return _peek = _text[start].ToString();
            while (_position < _text.Length && !char.IsWhiteSpace(_text[_position]) && _text[_position] is not ('{' or '}')) _position++;
            return _peek = _text[start.._position];
        }
    }
}
