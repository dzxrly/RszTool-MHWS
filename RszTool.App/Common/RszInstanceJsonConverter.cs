using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;

namespace RszTool.App.Common
{
    public class RszInstanceJsonConverter : JsonConverter<RszInstance>
    {
        private HashSet<int> processedInstances = new();

        public override RszInstance? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException("反序列化暂不支持");
        }

        public override void Write(Utf8JsonWriter writer, RszInstance value, JsonSerializerOptions options)
        {
            processedInstances.Clear();
            WriteInstance(writer, value);
        }

        private void WriteInstance(Utf8JsonWriter writer, RszInstance instance)
        {
            writer.WriteStartObject();
            
            writer.WriteString("name", instance.Name);
            writer.WriteNumber("index", instance.Index);
            writer.WriteNumber("objectTableIndex", instance.ObjectTableIndex);
            writer.WriteString("className", instance.RszClass.name);

            writer.WritePropertyName("fields");
            writer.WriteStartObject();

            for (int i = 0; i < instance.Fields.Length; i++)
            {
                WriteField(writer, instance.Fields[i], instance.Values[i]);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        private void WriteField(Utf8JsonWriter writer, RszField field, object value)
        {
            if (field.array)
            {
                writer.WritePropertyName(field.name);
                WriteArrayValue(writer, field, value);
            }
            else if (field.IsReference && value is RszInstance refInstance)
            {
                writer.WritePropertyName(field.name);
                writer.WriteStartObject();
                writer.WriteString("type", field.DisplayType);
                writer.WritePropertyName("value");
                WriteReferenceInstanceValue(writer, refInstance);
                writer.WriteEndObject();
            }
            else
            {
                writer.WritePropertyName(field.name);
                WriteNormalValue(writer, field, value);
            }
        }

        private void WriteArrayValue(Utf8JsonWriter writer, RszField field, object value)
        {
            writer.WriteStartArray();
            if (value is List<object> list)
            {
                foreach (var item in list)
                {
                    if (field.IsReference && item is RszInstance instance)
                    {
                        WriteReferenceInstanceValue(writer, instance);
                    }
                    else
                    {
                        WriteNormalValue(writer, field, item);
                    }
                }
            }
            writer.WriteEndArray();
        }

        private void WriteReferenceInstanceValue(Utf8JsonWriter writer, RszInstance instance)
        {
            writer.WriteStartObject();
            
            writer.WriteString("referenceType", "instance");
            writer.WriteString("name", instance.Name);
            writer.WriteNumber("index", instance.Index);

            if (instance.Fields.Length > 0)
            {
                writer.WritePropertyName("fields");
                writer.WriteStartObject();
                
                for (int i = 0; i < instance.Fields.Length; i++)
                {
                    WriteField(writer, instance.Fields[i], instance.Values[i]);
                }
                
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        private void WriteNormalValue(Utf8JsonWriter writer, RszField field, object value)
        {
            switch (field.type)
            {
                case RszFieldType.String:
                    writer.WriteStringValue((string)value);
                    break;
                case RszFieldType.Data:
                    writer.WriteStringValue(Convert.ToBase64String((byte[])value));
                    break;
                case RszFieldType.Vec2:
                case RszFieldType.Float2:
                case RszFieldType.Point:
                    var vec2 = (Vector2)value;
                    writer.WriteStartObject();
                    writer.WriteNumber("x", vec2.X);
                    writer.WriteNumber("y", vec2.Y);
                    writer.WriteEndObject();
                    break;
                case RszFieldType.Vec3:
                case RszFieldType.Float3:
                    var vec3 = (Vector3)value;
                    writer.WriteStartObject();
                    writer.WriteNumber("x", vec3.X);
                    writer.WriteNumber("y", vec3.Y);
                    writer.WriteNumber("z", vec3.Z);
                    writer.WriteEndObject();
                    break;
                case RszFieldType.Vec4:
                case RszFieldType.Float4:
                    var vec4 = (Vector4)value;
                    writer.WriteStartObject();
                    writer.WriteNumber("x", vec4.X);
                    writer.WriteNumber("y", vec4.Y);
                    writer.WriteNumber("z", vec4.Z);
                    writer.WriteNumber("w", vec4.W);
                    writer.WriteEndObject();
                    break;
                case RszFieldType.Bool:
                    writer.WriteBooleanValue((bool)value);
                    break;
                case RszFieldType.S8:
                    writer.WriteNumberValue((sbyte)value);
                    break;
                case RszFieldType.U8:
                    writer.WriteNumberValue((byte)value);
                    break;
                case RszFieldType.S16:
                    writer.WriteNumberValue((short)value);
                    break;
                case RszFieldType.U16:
                    writer.WriteNumberValue((ushort)value);
                    break;
                case RszFieldType.S32:
                    writer.WriteNumberValue((int)value);
                    break;
                case RszFieldType.U32:
                    writer.WriteNumberValue((uint)value);
                    break;
                case RszFieldType.F32:
                    writer.WriteNumberValue((float)value);
                    break;
                case RszFieldType.S64:
                    writer.WriteNumberValue((long)value);
                    break;
                case RszFieldType.U64:
                    writer.WriteNumberValue((ulong)value);
                    break;
                case RszFieldType.F64:
                    writer.WriteNumberValue((double)value);
                    break;
                default:
                    writer.WriteStringValue(value.ToString());
                    break;
            }
        }
    }

    public static class RszInstanceExtensions
    {
        public static string ToJson(this RszInstance instance)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new RszInstanceJsonConverter() }
            };

            return JsonSerializer.Serialize(instance, options);
        }

        public static string ToJson(this IEnumerable<RszInstance> instances)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new RszInstanceJsonConverter() }
            };

            return JsonSerializer.Serialize(instances, options);
        }
    }
}