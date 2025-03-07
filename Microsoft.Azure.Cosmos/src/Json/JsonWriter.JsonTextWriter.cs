﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Json
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Partial class for the JsonWriter that has a private JsonTextWriter below.
    /// </summary>
#if INTERNAL
    public
#else
    internal
#endif
    abstract partial class JsonWriter : IJsonWriter
    {
        /// <summary>
        /// This class is used to build a JSON string.
        /// It supports our defined IJsonWriter interface.
        /// It keeps an stack to keep track of scope, and provides error checking using that.
        /// It has few other variables for error checking
        /// The user can also provide initial size to reserve string buffer, that will help reduce cost of reallocation.
        /// It provides error checking based on JSON grammar. It provides escaping for nine characters specified in JSON.
        /// </summary>
        private sealed class JsonTextWriter : JsonWriter
        {
            private const int MaxStackAlloc = 4 * 1024;

            private const byte ValueSeperatorToken = (byte)':';
            private const byte MemberSeperatorToken = (byte)',';
            private const byte ObjectStartToken = (byte)'{';
            private const byte ObjectEndToken = (byte)'}';
            private const byte ArrayStartToken = (byte)'[';
            private const byte ArrayEndToken = (byte)']';
            private const byte PropertyStartToken = (byte)'"';
            private const byte PropertyEndToken = (byte)'"';
            private const byte StringStartToken = (byte)'"';
            private const byte StringEndToken = (byte)'"';

            private const byte Int8TokenPrefix = (byte)'I';
            private const byte Int16TokenPrefix = (byte)'H';
            private const byte Int32TokenPrefix = (byte)'L';
            private const byte UnsignedTokenPrefix = (byte)'U';
            private const byte FloatTokenPrefix = (byte)'S';
            private const byte DoubleTokenPrefix = (byte)'D';
            private const byte GuidTokenPrefix = (byte)'G';
            private const byte BinaryTokenPrefix = (byte)'B';

            private static readonly ReadOnlyMemory<byte> NotANumber = new byte[]
            {
                (byte)'N', (byte)'a', (byte)'N'
            };
            private static readonly ReadOnlyMemory<byte> PositiveInfinity = new byte[]
            {
                (byte)'I', (byte)'n', (byte)'f', (byte)'i', (byte)'n', (byte)'i', (byte)'t', (byte)'y'
            };
            private static readonly ReadOnlyMemory<byte> NegativeInfinity = new byte[]
            {
                (byte)'-', (byte)'I', (byte)'n', (byte)'f', (byte)'i', (byte)'n', (byte)'i', (byte)'t', (byte)'y'
            };
            private static readonly ReadOnlyMemory<byte> TrueString = new byte[]
            {
                (byte)'t', (byte)'r', (byte)'u', (byte)'e'
            };
            private static readonly ReadOnlyMemory<byte> FalseString = new byte[]
            {
                (byte)'f', (byte)'a', (byte)'l', (byte)'s', (byte)'e'
            };
            private static readonly ReadOnlyMemory<byte> NullString = new byte[]
            {
                (byte)'n', (byte)'u', (byte)'l', (byte)'l'
            };

            /// <summary>
            /// JSON needs to escape quote, reverse soldius, and control characters (anything less than space).
            /// </summary>
            private static readonly byte[] CharactersThatNeedEscaping = Enumerable.Range(0, (int)' ').Select(x => (byte)x).Concat(new byte[] { (byte)'"', (byte)'\\' }).ToArray();

            private readonly JsonTextMemoryWriter jsonTextMemoryWriter;

            /// <summary>
            /// Whether we are writing the first value of an array or object
            /// </summary>
            private bool firstValue;

            /// <summary>
            /// Initializes a new instance of the JsonTextWriter class.
            /// </summary>
            /// <param name="skipValidation">Whether or not to skip validation</param>
            public JsonTextWriter(bool skipValidation)
                : base(skipValidation)
            {
                this.firstValue = true;
                this.jsonTextMemoryWriter = new JsonTextMemoryWriter();
            }

            /// <inheritdoc />
            public override JsonSerializationFormat SerializationFormat
            {
                get
                {
                    return JsonSerializationFormat.Text;
                }
            }

            /// <inheritdoc />
            public override long CurrentLength
            {
                get
                {
                    return this.jsonTextMemoryWriter.Position;
                }
            }

            /// <inheritdoc />
            public override void WriteObjectStart()
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.BeginObject);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(ObjectStartToken);
                this.firstValue = true;
            }

            /// <inheritdoc />
            public override void WriteObjectEnd()
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.EndObject);
                this.jsonTextMemoryWriter.Write(ObjectEndToken);

                // We reset firstValue here because we'll need a separator before the next value
                this.firstValue = false;
            }

            /// <inheritdoc />
            public override void WriteArrayStart()
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.BeginArray);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(ArrayStartToken);
                this.firstValue = true;
            }

            /// <inheritdoc />
            public override void WriteArrayEnd()
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.EndArray);
                this.jsonTextMemoryWriter.Write(ArrayEndToken);

                // We reset firstValue here because we'll need a separator before the next value
                this.firstValue = false;
            }

            /// <inheritdoc />
            public override unsafe void WriteFieldName(string fieldName)
            {
                int utf8Length = Encoding.UTF8.GetByteCount(fieldName);
                Span<byte> utf8FieldName = utf8Length < JsonTextWriter.MaxStackAlloc ? stackalloc byte[utf8Length] : new byte[utf8Length];
                Encoding.UTF8.GetBytes(fieldName, utf8FieldName);

                this.WriteFieldName(utf8FieldName);
            }

            /// <inheritdoc />
            public override void WriteFieldName(ReadOnlySpan<byte> utf8FieldName)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.FieldName);
                this.PrefixMemberSeparator();

                // no separator after property name
                this.firstValue = true;

                this.jsonTextMemoryWriter.Write(PropertyStartToken);
                this.WriteEscapedString(utf8FieldName);
                this.jsonTextMemoryWriter.Write(PropertyEndToken);

                this.jsonTextMemoryWriter.Write(ValueSeperatorToken);
            }

            /// <inheritdoc />
            public override void WriteStringValue(string value)
            {
                int utf8Length = Encoding.UTF8.GetByteCount(value);
                Span<byte> utf8String = utf8Length < JsonTextWriter.MaxStackAlloc ? stackalloc byte[utf8Length] : new byte[utf8Length];
                Encoding.UTF8.GetBytes(value, utf8String);

                this.WriteStringValue(utf8String);
            }

            /// <inheritdoc />
            public override void WriteStringValue(ReadOnlySpan<byte> utf8StringValue)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.String);
                this.PrefixMemberSeparator();

                this.jsonTextMemoryWriter.Write(StringStartToken);
                this.WriteEscapedString(utf8StringValue);
                this.jsonTextMemoryWriter.Write(StringEndToken);
            }

            /// <inheritdoc />
            public override void WriteNumber64Value(Number64 value)
            {
                if (value.IsInteger)
                {
                    this.WriteIntegerInternal(Number64.ToLong(value));
                }
                else
                {
                    this.WriteDoubleInternal(Number64.ToDouble(value));
                }
            }

            /// <inheritdoc />
            public override void WriteBoolValue(bool value)
            {
                this.JsonObjectState.RegisterToken(value ? JsonTokenType.True : JsonTokenType.False);
                this.PrefixMemberSeparator();

                if (value)
                {
                    this.jsonTextMemoryWriter.Write(TrueString.Span);
                }
                else
                {
                    this.jsonTextMemoryWriter.Write(FalseString.Span);
                }
            }

            /// <inheritdoc />
            public override void WriteNullValue()
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Null);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(NullString.Span);
            }

            /// <inheritdoc />
            public override void WriteInt8Value(sbyte value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Int8);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(Int8TokenPrefix);
                this.jsonTextMemoryWriter.Write(value);
            }

            /// <inheritdoc />
            public override void WriteInt16Value(short value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Int16);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(Int16TokenPrefix);
                this.jsonTextMemoryWriter.Write(value);
            }

            /// <inheritdoc />
            public override void WriteInt32Value(int value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Int32);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(Int32TokenPrefix);
                this.jsonTextMemoryWriter.Write(value);
            }

            /// <inheritdoc />
            public override void WriteInt64Value(long value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Int64);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(Int32TokenPrefix);
                this.jsonTextMemoryWriter.Write(Int32TokenPrefix);
                this.jsonTextMemoryWriter.Write(value);
            }

            /// <inheritdoc />
            public override void WriteFloat32Value(float value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Float32);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(FloatTokenPrefix);
                this.jsonTextMemoryWriter.Write(value);
            }

            /// <inheritdoc />
            public override void WriteFloat64Value(double value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Float64);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(DoubleTokenPrefix);
                this.jsonTextMemoryWriter.Write(value);
            }

            /// <inheritdoc />
            public override void WriteUInt32Value(uint value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.UInt32);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(UnsignedTokenPrefix);
                this.jsonTextMemoryWriter.Write(Int32TokenPrefix);
                this.jsonTextMemoryWriter.Write(value);
            }

            /// <inheritdoc />
            public override void WriteGuidValue(Guid value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Guid);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(GuidTokenPrefix);
                this.jsonTextMemoryWriter.Write(value);
            }

            /// <inheritdoc />
            public override void WriteBinaryValue(ReadOnlySpan<byte> value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Binary);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(BinaryTokenPrefix);
                this.jsonTextMemoryWriter.WriteBinaryAsBase64(value);
            }

            /// <inheritdoc />
            public override ReadOnlyMemory<byte> GetResult()
            {
                return this.jsonTextMemoryWriter.Buffer.Slice(
                    0,
                    this.jsonTextMemoryWriter.Position);
            }

            /// <inheritdoc />
            protected override void WriteRawJsonToken(
                JsonTokenType jsonTokenType,
                ReadOnlySpan<byte> rawJsonToken)
            {
                switch (jsonTokenType)
                {
                    case JsonTokenType.String:
                    case JsonTokenType.Number:
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Null:
                    case JsonTokenType.FieldName:
                    case JsonTokenType.Int8:
                    case JsonTokenType.Int16:
                    case JsonTokenType.Int32:
                    case JsonTokenType.Int64:
                    case JsonTokenType.UInt32:
                    case JsonTokenType.Float32:
                    case JsonTokenType.Float64:
                    case JsonTokenType.Guid:
                    case JsonTokenType.Binary:
                        // Supported Tokens
                        break;
                    default:
                        throw new ArgumentOutOfRangeException($"Unknown token type: {jsonTokenType}.");
                }

                if (rawJsonToken.IsEmpty)
                {
                    throw new ArgumentException($"Expected non empty {nameof(rawJsonToken)}.");
                }

                this.JsonObjectState.RegisterToken(jsonTokenType);
                this.PrefixMemberSeparator();

                // No separator after property name
                if (jsonTokenType == JsonTokenType.FieldName)
                {
                    this.firstValue = true;
                    this.jsonTextMemoryWriter.Write(rawJsonToken);
                    this.jsonTextMemoryWriter.Write(ValueSeperatorToken);
                }
                else
                {
                    this.jsonTextMemoryWriter.Write(rawJsonToken);
                }
            }

            private void WriteIntegerInternal(long value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Number);
                this.PrefixMemberSeparator();
                this.jsonTextMemoryWriter.Write(value);
            }

            private void WriteDoubleInternal(double value)
            {
                this.JsonObjectState.RegisterToken(JsonTokenType.Number);
                this.PrefixMemberSeparator();
                if (double.IsNaN(value))
                {
                    this.jsonTextMemoryWriter.Write(StringStartToken);
                    this.jsonTextMemoryWriter.Write(NotANumber.Span);
                    this.jsonTextMemoryWriter.Write(StringEndToken);
                }
                else if (double.IsNegativeInfinity(value))
                {
                    this.jsonTextMemoryWriter.Write(StringStartToken);
                    this.jsonTextMemoryWriter.Write(NegativeInfinity.Span);
                    this.jsonTextMemoryWriter.Write(StringEndToken);
                }
                else if (double.IsPositiveInfinity(value))
                {
                    this.jsonTextMemoryWriter.Write(StringStartToken);
                    this.jsonTextMemoryWriter.Write(PositiveInfinity.Span);
                    this.jsonTextMemoryWriter.Write(StringEndToken);
                }
                else
                {
                    this.jsonTextMemoryWriter.Write(value);
                }
            }

            private void PrefixMemberSeparator()
            {
                if (!this.firstValue)
                {
                    this.jsonTextMemoryWriter.Write(MemberSeperatorToken);
                }

                this.firstValue = false;
            }

            private void WriteEscapedString(ReadOnlySpan<byte> unescapedString)
            {
                while (!unescapedString.IsEmpty)
                {
                    int? indexOfFirstCharacterThatNeedsEscaping = JsonTextWriter.IndexOfCharacterThatNeedsEscaping(unescapedString);
                    if (!indexOfFirstCharacterThatNeedsEscaping.HasValue)
                    {
                        // No escaping needed;
                        indexOfFirstCharacterThatNeedsEscaping = unescapedString.Length;
                    }

                    // Write as much of the string as possible
                    this.jsonTextMemoryWriter.Write(
                        unescapedString.Slice(
                            start: 0,
                            length: indexOfFirstCharacterThatNeedsEscaping.Value));
                    unescapedString = unescapedString.Slice(start: indexOfFirstCharacterThatNeedsEscaping.Value);

                    // Escape the next character if it exists
                    if (!unescapedString.IsEmpty)
                    {
                        byte character = unescapedString[0];
                        unescapedString = unescapedString.Slice(start: 1);

                        switch (character)
                        {
                            case (byte)'\\':
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                break;

                            case (byte)'"':
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                this.jsonTextMemoryWriter.Write((byte)'"');
                                break;

                            case (byte)'/':
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                this.jsonTextMemoryWriter.Write((byte)'/');
                                break;

                            case (byte)'\b':
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                this.jsonTextMemoryWriter.Write((byte)'b');
                                break;

                            case (byte)'\f':
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                this.jsonTextMemoryWriter.Write((byte)'f');
                                break;

                            case (byte)'\n':
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                this.jsonTextMemoryWriter.Write((byte)'n');
                                break;

                            case (byte)'\r':
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                this.jsonTextMemoryWriter.Write((byte)'r');
                                break;

                            case (byte)'\t':
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                this.jsonTextMemoryWriter.Write((byte)'t');
                                break;

                            default:
                                char wideCharToEscape = (char)character;
                                // We got a control character (U+0000 through U+001F).
                                this.jsonTextMemoryWriter.Write((byte)'\\');
                                this.jsonTextMemoryWriter.Write((byte)'u');
                                this.jsonTextMemoryWriter.Write(GetHexDigit((wideCharToEscape >> 12) & 0xF));
                                this.jsonTextMemoryWriter.Write(GetHexDigit((wideCharToEscape >> 8) & 0xF));
                                this.jsonTextMemoryWriter.Write(GetHexDigit((wideCharToEscape >> 4) & 0xF));
                                this.jsonTextMemoryWriter.Write(GetHexDigit((wideCharToEscape >> 0) & 0xF));
                                break;
                        }
                    }
                }
            }

            private static int? IndexOfCharacterThatNeedsEscaping(ReadOnlySpan<byte> span)
            {
                int? index = null;

                int indexOfAny = span.IndexOfAny(JsonTextWriter.CharactersThatNeedEscaping.AsSpan());
                if (indexOfAny != -1)
                {
                    index = indexOfAny;
                }

                return index;
            }

            private static byte GetHexDigit(int value)
            {
                return (byte)((value < 10) ? '0' + value : 'A' + value - 10);
            }

            private sealed class JsonTextMemoryWriter : JsonMemoryWriter
            {
                private static readonly StandardFormat floatFormat = new StandardFormat(
                    symbol: 'R');

                private static readonly StandardFormat doubleFormat = new StandardFormat(
                    symbol: 'R');

                public JsonTextMemoryWriter(int initialCapacity = 256)
                    : base(initialCapacity)
                {
                }

                public void Write(bool value)
                {
                    const int MaxBoolLength = 5;
                    this.EnsureRemainingBufferSpace(MaxBoolLength);
                    if (!Utf8Formatter.TryFormat(value, this.Cursor.Span, out int bytesWritten))
                    {
                        throw new InvalidOperationException($"Failed to {nameof(this.Write)}({typeof(bool).FullName}{value})");
                    }

                    this.Position += bytesWritten;
                }

                public void Write(byte value)
                {
                    this.EnsureRemainingBufferSpace(1);
                    this.Buffer.Span[this.Position] = value;
                    this.Position++;
                }

                public void Write(sbyte value)
                {
                    const int MaxInt8Length = 4;
                    this.EnsureRemainingBufferSpace(MaxInt8Length);
                    if (!Utf8Formatter.TryFormat(value, this.Cursor.Span, out int bytesWritten))
                    {
                        throw new InvalidOperationException($"Failed to {nameof(this.Write)}({typeof(sbyte).FullName}{value})");
                    }

                    this.Position += bytesWritten;
                }

                public void Write(short value)
                {
                    const int MaxInt16Length = 6;
                    this.EnsureRemainingBufferSpace(MaxInt16Length);
                    if (!Utf8Formatter.TryFormat(value, this.Cursor.Span, out int bytesWritten))
                    {
                        throw new InvalidOperationException($"Failed to {nameof(this.Write)}({typeof(short).FullName}{value})");
                    }

                    this.Position += bytesWritten;
                }

                public void Write(int value)
                {
                    const int MaxInt32Length = 11;
                    this.EnsureRemainingBufferSpace(MaxInt32Length);
                    if (!Utf8Formatter.TryFormat(value, this.Cursor.Span, out int bytesWritten))
                    {
                        throw new InvalidOperationException($"Failed to {nameof(this.Write)}({typeof(int).FullName}{value})");
                    }

                    this.Position += bytesWritten;
                }

                public void Write(uint value)
                {
                    const int MaxInt32Length = 11;
                    this.EnsureRemainingBufferSpace(MaxInt32Length);
                    if (!Utf8Formatter.TryFormat(value, this.Cursor.Span, out int bytesWritten))
                    {
                        throw new InvalidOperationException($"Failed to {nameof(this.Write)}({typeof(int).FullName}{value})");
                    }

                    this.Position += bytesWritten;
                }

                public void Write(long value)
                {
                    const int MaxInt64Length = 20;
                    this.EnsureRemainingBufferSpace(MaxInt64Length);
                    if (!Utf8Formatter.TryFormat(value, this.Cursor.Span, out int bytesWritten))
                    {
                        throw new InvalidOperationException($"Failed to {nameof(this.Write)}({typeof(long).FullName}{value})");
                    }

                    this.Position += bytesWritten;
                }

                public void Write(float value)
                {
                    const int MaxNumberLength = 32;
                    this.EnsureRemainingBufferSpace(MaxNumberLength);
                    // Can't use Utf8Formatter until we bump to core 3.0, since they don't support float.ToString("G9")
                    // Also for the 2.0 shim they are creating an intermediary string anyways
                    string floatString = value.ToString("R", CultureInfo.InvariantCulture);
                    for (int index = 0; index < floatString.Length; index++)
                    {
                        // we can cast to byte, since it's all ascii
                        this.Cursor.Span[0] = (byte)floatString[index];
                        this.Position++;
                    }
                }

                public void Write(double value)
                {
                    const int MaxNumberLength = 32;
                    this.EnsureRemainingBufferSpace(MaxNumberLength);
                    // Can't use Utf8Formatter until we bump to core 3.0, since they don't support float.ToString("R")
                    // Also for the 2.0 shim they are creating an intermediary string anyways
                    string doubleString = value.ToString("R", CultureInfo.InvariantCulture);
                    for (int index = 0; index < doubleString.Length; index++)
                    {
                        // we can cast to byte, since it's all ascii
                        this.Cursor.Span[0] = (byte)doubleString[index];
                        this.Position++;
                    }
                }

                public void Write(Guid value)
                {
                    const int GuidLength = 38;
                    this.EnsureRemainingBufferSpace(GuidLength);
                    if (!Utf8Formatter.TryFormat(value, this.Cursor.Span, out int bytesWritten))
                    {
                        throw new InvalidOperationException($"Failed to {nameof(this.Write)}({typeof(double).FullName}{value})");
                    }

                    this.Position += bytesWritten;
                }

                public void WriteBinaryAsBase64(ReadOnlySpan<byte> binary)
                {
                    this.EnsureRemainingBufferSpace(Base64.GetMaxEncodedToUtf8Length(binary.Length));
                    Base64.EncodeToUtf8(binary, this.Cursor.Span, out int bytesConsumed, out int bytesWritten);

                    this.Position += bytesWritten;
                }
            }
        }
    }
}