using System.Numerics;
using System.Text;
using RszTool.Common;

namespace RszTool
{
    /// <summary>
    /// 存放RszClass的数据
    /// </summary>
    public class RszInstance : BaseModel, ICloneable
    {
        public RszClass RszClass { get; set; }
        public object[] Values { get; set; }
        private int index;
        public int Index
        {
            get => index;
            set
            {
                index = value;
                name = null;
            }
        }
        // 在ObjectTable中的序号，-1表示不在
        public int ObjectTableIndex { get; set; } = -1;
        private string? name;
        public string Name => name ??= $"{RszClass.name}[{index}]";
        public IRSZUserDataInfo? RSZUserData { get; set; }
        public RszField[] Fields => RszClass.fields;

        public RszInstance(RszClass rszClass, int index = -1, IRSZUserDataInfo? userData = null, object[]? values = null)
        {
            RszClass = rszClass;
            if (userData == null && values != null)
            {
                if (values.Length != rszClass.fields.Length)
                {
                    throw new ArgumentException($"values length {values.Length} != fields length {rszClass.fields.Length}");
                }
            }
            Values = userData == null ? (values ?? new object[rszClass.fields.Length]) : [];
            Index = index;
            RSZUserData = userData;
        }

        private RszInstance()
        {
            RszClass = RszClass.Empty;
            Values = [];
        }

        /// <summary>
        /// 一般InstanceList第一个对象是NULL实例
        /// </summary>
        public static readonly RszInstance NULL = new();

        private void AlignFirstField(FileHandler handler)
        {
            handler.Align(RszClass.fields[0].array ? 4 : RszClass.fields[0].align);
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        protected override bool DoRead(FileHandler handler)
        {
            // if has RSZUserData, it is external
            if (RSZUserData != null || RszClass.fields.Length == 0) return true;

            AlignFirstField(handler);
            // Console.WriteLine($"read {Name} at: {handler.Position:X}");
            for (int i = 0; i < RszClass.fields.Length; i++)
            {
                Values[i] = ReadRszField(handler, i);
            }
            return true;
        }

        /// <summary>
        /// 读取字段
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        public object ReadRszField(FileHandler handler, int index)
        {
            RszField field = RszClass.fields[index];
            handler.Align(field.array ? 4 : field.align);
            // Console.WriteLine($"    read at: {handler.Position:X} {field.original_type} {field.name}");
            if (field.array)
            {
                int count = handler.ReadInt();
                if (count < 0)
                {
                    throw new InvalidDataException($"{field.name} count {count} < 0");
                }
                if (count > 1024)
                {
                    throw new InvalidDataException($"{field.name} count {count} too large");
                }
                List<object> arrayItems = new(count);
                if (count > 0) handler.Align(field.align);
                for (int i = 0; i < count; i++)
                {
                    if (field.IsString)
                    {
                        handler.Align(4);
                    }
                    object value = ReadNormalField(handler, field);
                    if (DetectDataType(field, ref value) && i > 0)
                    {
                        // 如果检测到其他类型，那么索引0的时候就应该检测出来了，说明之前检测的不对？
                        throw new InvalidDataException($"Detect {RszClass.name}.{field.name} as {field.type}, but index > 0");
                    }
                    arrayItems.Add(value);
                }
                return arrayItems;
            }
            else
            {
                object value = ReadNormalField(handler, field);
                DetectDataType(field, ref value);
                return value;
            }
        }

        /// <summary>
        /// 推断type是Data的字段的实际类型
        /// </summary>
        private bool DetectDataType(RszField field, ref object data)
        {
            if (field.type == RszFieldType.Data && field.size == 4 && field.native)
            {
                int intValue = BitConverter.ToInt32((byte[])data, 0);
                // 检测Object
                if (intValue < Index && intValue > 3 && intValue > Index - 101)
                {
                    field.type = RszFieldType.Object;
                    field.IsTypeInferred = true;
                    data = intValue;
                    Console.WriteLine($"Detect {Name}.{field.name} as Object");
                    return true;
                }
                // 检测float和int
                else if (Utils.DetectFloat((byte[])data, out float floatValue))
                {
                    field.type = RszFieldType.F32;
                    field.IsTypeInferred = true;
                    data = floatValue;
                }
                else
                {
                    field.type = RszFieldType.S32;
                    field.IsTypeInferred = true;
                    data = intValue;
                }
            }
            return false;
        }

        public static object ReadNormalField(FileHandler handler, RszField field)
        {
            if (field.IsString)
            {
                int charCount = handler.ReadInt();
                long stringStart = handler.Tell();
                string value = charCount <= 1 ? "" : handler.ReadWString(charCount: charCount);
                handler.Seek(stringStart + charCount * 2);
                // TODO checkOpenResource
                return value;
            }
            else
            {
                long startPos = handler.Tell();
                object value = field.type switch
                {
                    RszFieldType.S32 or RszFieldType.Object or RszFieldType.UserData => handler.ReadInt(),
                    RszFieldType.U32 => handler.ReadUInt(),
                    RszFieldType.S64 => handler.ReadInt64(),
                    RszFieldType.U64 => handler.ReadUInt64(),
                    RszFieldType.F32 => handler.ReadFloat(),
                    RszFieldType.F64 => handler.ReadDouble(),
                    RszFieldType.Bool => handler.ReadBoolean(),
                    RszFieldType.S8 => handler.ReadSByte(),
                    RszFieldType.U8 => handler.ReadByte(),
                    RszFieldType.S16 => handler.ReadShort(),
                    RszFieldType.U16 => handler.ReadUShort(),
                    RszFieldType.Data => handler.ReadBytes(field.size),
                    RszFieldType.Mat4 => handler.Read<via.mat4>(),
                    RszFieldType.Vec2 or RszFieldType.Float2 => handler.Read<Vector2>(),
                    RszFieldType.Vec3 or RszFieldType.Float3 => handler.Read<Vector3>(),
                    RszFieldType.Vec4 or RszFieldType.Float4 => handler.Read<Vector4>(),
                    RszFieldType.OBB => handler.Read<via.OBB>(),
                    RszFieldType.AABB => handler.Read<via.AABB>(),
                    RszFieldType.Guid or RszFieldType.GameObjectRef => handler.Read<Guid>(),
                    RszFieldType.Color => handler.Read<via.Color>(),
                    RszFieldType.Range => handler.Read<via.Range>(),
                    RszFieldType.Quaternion => handler.Read<Quaternion>(),
                    RszFieldType.Sphere => handler.Read<via.Sphere>(),
                    RszFieldType.Capsule => handler.Read<via.Capsule>(),
                    RszFieldType.Area => handler.Read<via.Area>(),
                    _ => throw new NotSupportedException($"Not support type {field.type}"),
                };
                /* if (field.size != handler.Tell() - startPos)
                {
                    throw new InvalidDataException($"{field.name} size not match");
                } */
                handler.Seek(startPos + field.size);
                return value;
            }
        }

        /// <summary>
        /// 写入数据
        /// </summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        protected override bool DoWrite(FileHandler handler)
        {
            // if has RSZUserData, it is external
            if (RSZUserData != null || RszClass.fields.Length == 0) return true;
            AlignFirstField(handler);
            // Console.WriteLine($"write {Name} at: {(handler.Offset + handler.Tell()):X}");
            for (int i = 0; i < RszClass.fields.Length; i++)
            {
                WriteRszField(handler, i);
            }
            return true;
        }

        /// <summary>
        /// 写入字段
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public bool WriteRszField(FileHandler handler, int index)
        {
            RszField field = RszClass.fields[index];
            handler.Align(field.array ? 4 : field.align);
            // Console.WriteLine($"    write at: {handler.Position:X} {field.original_type} {field.name}");
            if (field.array)
            {
                List<object> list = (List<object>)Values[index];
                handler.Write(list.Count);
                if (list.Count > 0)
                {
                    handler.Align(field.align);
                    foreach (var value in list)
                    {
                        if (field.IsString)
                        {
                            handler.Align(4);
                        }
                        WriteNormalField(handler, field, value);
                    }
                }
                return true;
            }
            else
            {
                return WriteNormalField(handler, field, Values[index]);
            }
        }

        public static bool WriteNormalField(FileHandler handler, RszField field, object value)
        {
            if (field.IsString)
            {
                string valueStr = (string)value;
                return handler.Write(valueStr.Length + 1) && handler.WriteWString(valueStr);
            }
            else if (field.type == RszFieldType.Object || field.type == RszFieldType.UserData)
            {
                return handler.Write(value is RszInstance instance ? instance.Index : (int)value);
            }
            else
            {
                long startPos = handler.Tell();
                _ = field.type switch
                {
                    RszFieldType.S32 => handler.Write((int)value),
                    RszFieldType.U32 => handler.Write((uint)value),
                    RszFieldType.S64 => handler.Write((long)value),
                    RszFieldType.U64 => handler.Write((ulong)value),
                    RszFieldType.F32 => handler.Write((float)value),
                    RszFieldType.F64 => handler.Write((double)value),
                    RszFieldType.Bool => handler.Write((bool)value),
                    RszFieldType.S8 => handler.Write((sbyte)value),
                    RszFieldType.U8 => handler.Write((byte)value),
                    RszFieldType.S16 => handler.Write((short)value),
                    RszFieldType.U16 => handler.Write((ushort)value),
                    RszFieldType.Data => handler.WriteBytes((byte[])value),
                    RszFieldType.Mat4 => handler.Write((via.mat4)value),
                    RszFieldType.Vec2 or RszFieldType.Float2 => handler.Write((Vector2)value),
                    RszFieldType.Vec3 or RszFieldType.Float3 => handler.Write((Vector3)value),
                    RszFieldType.Vec4 or RszFieldType.Float4 => handler.Write((Vector4)value),
                    RszFieldType.OBB => handler.Write((via.OBB)value),
                    RszFieldType.AABB => handler.Write((via.AABB)value),
                    RszFieldType.Guid or RszFieldType.GameObjectRef => handler.Write((Guid)value),
                    RszFieldType.Color => handler.Write((via.Color)value),
                    RszFieldType.Range => handler.Write((via.Range)value),
                    RszFieldType.Quaternion => handler.Write((Quaternion)value),
                    RszFieldType.Sphere => handler.Write((via.Sphere)value),
                    RszFieldType.Capsule => handler.Write((via.Capsule)value),
                    RszFieldType.Area => handler.Write((via.Area)value),
                    _ => throw new NotSupportedException($"Not support type {field.type}"),
                };
                handler.Seek(startPos + field.size);
                return true;
            }
        }

        public static Type RszFieldTypeToCSharpType(RszFieldType type)
        {
            return type switch
            {
                RszFieldType.S32 or RszFieldType.Object or RszFieldType.UserData => typeof(int),
                RszFieldType.U32 => typeof(uint),
                RszFieldType.S64 => typeof(long),
                RszFieldType.U64 => typeof(ulong),
                RszFieldType.F32 => typeof(float),
                RszFieldType.F64 => typeof(double),
                RszFieldType.Bool => typeof(bool),
                RszFieldType.S8 => typeof(sbyte),
                RszFieldType.U8 => typeof(byte),
                RszFieldType.S16 => typeof(short),
                RszFieldType.U16 => typeof(ushort),
                RszFieldType.Data => typeof(byte[]),
                RszFieldType.Mat4 => typeof(via.mat4),
                RszFieldType.Vec2 or RszFieldType.Float2 => typeof(Vector2),
                RszFieldType.Vec3 or RszFieldType.Float3 => typeof(Vector3),
                RszFieldType.Vec4 or RszFieldType.Float4 => typeof(Vector4),
                RszFieldType.AABB => typeof(via.AABB),
                RszFieldType.Guid or RszFieldType.GameObjectRef => typeof(Guid),
                RszFieldType.Color => typeof(via.Color),
                RszFieldType.Range => typeof(via.Range),
                RszFieldType.Quaternion => typeof(Quaternion),
                RszFieldType.Sphere => typeof(via.Sphere),
                RszFieldType.Capsule => typeof(via.Capsule),
                RszFieldType.Area => typeof(via.Area),
                RszFieldType.String or RszFieldType.Resource => typeof(string),
                _ => throw new NotSupportedException($"Not support type {type}"),
            };
        }

        /*
        private static readonly Dictionary<Type, RszFieldType> CSharpTypeToRszFieldFieldTypeDict = new()
        {
            [typeof(int)] = RszFieldType.S32,
            [typeof(uint)] = RszFieldType.U32,
            [typeof(long)] = RszFieldType.S64,
            [typeof(ulong)] = RszFieldType.U64,
            [typeof(float)] = RszFieldType.F32,
            [typeof(double)] = RszFieldType.F64,
            [typeof(bool)] = RszFieldType.Bool,
            [typeof(sbyte)] = RszFieldType.S8,
            [typeof(byte)] = RszFieldType.U8,
            [typeof(short)] = RszFieldType.S16,
            [typeof(ushort)] = RszFieldType.U16,
            [typeof(byte[])] = RszFieldType.Data,
            [typeof(via.mat4)] = RszFieldType.Mat4,
            [typeof(Vector2)] = RszFieldType.Vec2,
            [typeof(Vector3)] = RszFieldType.Vec3,
            [typeof(Vector4)] = RszFieldType.Vec4,
            [typeof(via.OBB)] = RszFieldType.OBB,
            [typeof(via.AABB)] = RszFieldType.AABB,
            [typeof(Guid)] = RszFieldType.Guid,
            [typeof(Color)] = RszFieldType.Color,
            [typeof(via.Range)] = RszFieldType.Range,
            [typeof(Quaternion)] = RszFieldType.Quaternion,
            [typeof(via.Sphere)] = RszFieldType.Sphere,
            [typeof(via.Capsule)] = RszFieldType.Capsule,
            [typeof(via.Area)] = RszFieldType.Area,
            [typeof(string)] = RszFieldType.String,
        };

        public static RszFieldType CSharpTypeToRszFieldFieldType(Type type)
        {
            RszFieldType fieldType = RszFieldType.ukn_type;
            CSharpTypeToRszFieldFieldTypeDict.TryGetValue(type, out fieldType);
            return fieldType;
        } */

        /// <summary>
        /// 获取字段值
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public object? GetFieldValue(string name)
        {
            int index = RszClass.IndexOfField(name);
            if (index == -1) return null;
            return Values[index];
        }

        /// <summary>
        /// 设置字段值
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        public void SetFieldValue(string name, object value)
        {
            int index = RszClass.IndexOfField(name);
            if (index == -1) return;
            Values[index] = value;
        }

        /// <summary>
        /// 避免被引用多次的instance拷贝多次
        /// 每次拷贝会话，比如ImportGameObject后应该清空
        /// </summary>
        public static Dictionary<RszInstance, RszInstance> CloneCache { get; } = new();

        public static void CleanCloneCache()
        {
            CloneCache.Clear();
        }

        /// <summary>
        /// 拷贝自身，如果字段值是RszInstance，则递归拷贝
        /// </summary>
        /// <returns></returns>
        public override object Clone()
        {
            return CloneImpl(false);
        }

        /// <summary>
        /// 拷贝自身，如果字段值是RszInstance，则递归拷贝
        /// </summary>
        /// <returns></returns>
        public RszInstance CloneCached()
        {
            return CloneImpl(true);
        }

        private RszInstance CloneImpl(bool cached)
        {
            if (cached && CloneCache.TryGetValue(this, out RszInstance? copy)) return copy;
            IRSZUserDataInfo? userData = RSZUserData != null ? (IRSZUserDataInfo)RSZUserData.Clone() : null;
            copy = new(RszClass, -1, userData);
            if (cached) CloneCache[this] = copy;
            Array.Copy(Values, copy.Values, Values.Length);
            if (userData == null)
            {
                for (int i = 0; i < RszClass.fields.Length; i++)
                {
                    var field = RszClass.fields[i];
                    if (field.array)
                    {
                        var newArray = new List<object>((List<object>)copy.Values[i]);
                        copy.Values[i] = newArray;
                        for (int j = 0; j < newArray.Count; j++)
                        {
                            if (field.IsReference && newArray[j] is RszInstance item)
                            {
                                newArray[j] = item.CloneImpl(cached);
                            }
                            else
                            {
                                newArray[j] = CloneValueType(newArray[j]);
                            }
                        }
                    }
                    else if (field.IsReference && copy.Values[i] is RszInstance instance)
                    {
                        copy.Values[i] = instance.CloneImpl(cached);
                    }
                    else
                    {
                        copy.Values[i] = CloneValueType(copy.Values[i]);
                    }
                }
            }
            return copy;
        }

        private static System.Reflection.MethodInfo? memberwiseClone;

        private static object CloneValueType(object value)
        {
            Type type = value.GetType();
            if (type.IsValueType && !type.IsPrimitive && !type.IsEnum)
            {
                memberwiseClone ??= typeof(object).GetMethod("MemberwiseClone",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (memberwiseClone != null)
                {
                    var newObject = memberwiseClone.Invoke(value, null);
                    if (newObject != null) return newObject;
                }
            }
            return value;
        }

        public IEnumerable<RszInstance> Flatten()
        {
            if (RSZUserData == null)
            {
                var fields = RszClass.fields;
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field.IsReference)
                    {
                        if (field.array)
                        {
                            var items = (List<object>)Values[i];
                            for (int j = 0; j < items.Count; j++)
                            {
                                if (items[j] is RszInstance instanceValue)
                                {
                                    foreach (var item in instanceValue.Flatten())
                                    {
                                        yield return item;
                                    }
                                }
                                else
                                {
                                    throw new InvalidOperationException("Instance should unflatten first");
                                }
                            }
                        }
                        else if (Values[i] is RszInstance instanceValue)
                        {
                            foreach (var item in instanceValue.Flatten())
                            {
                                yield return item;
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("Instance should unflatten first");
                        }
                    }
                }
            }
            yield return this;
        }

        public void Stringify(StringBuilder sb, IList<RszInstance>? instances = null, int indent = 0)
        {
            if (RszClass.crc == 0)
            {
                sb.Append("NULL");
                return;
            }
            sb.Append(Name);
            sb.AppendLine(" {");

            void ValueStringify(RszField field, object value)
            {
                if (field.IsReference)
                {
                    if (field.array)
                    {
                        sb.AppendLine();
                        sb.AppendIndent(indent + 2);
                    }
                    if (value is int index && instances != null)
                    {
                        instances[(int)value].Stringify(sb, instances, indent + 2);
                    }
                    else if (value is RszInstance instance)
                    {
                        instance.Stringify(sb, instances, indent + 2);
                    }
                    else
                    {
                        sb.Append(value);
                    }
                }
                else
                {
                    sb.Append(value);
                }
            }

            if (RSZUserData != null)
            {
                if (RSZUserData is RSZUserDataInfo info)
                {
                    sb.AppendIndent(indent + 1);
                    sb.AppendLine($"RSZUserDataPath = {info.Path}");
                }
            }
            else
            {
                for (int i = 0; i < RszClass.fields.Length; i++)
                {
                    RszField field = RszClass.fields[i];
                    string type = field.DisplayType;
                    sb.AppendIndent(indent + 1);
                    sb.Append($"{type} {field.name} = ");
                    if (field.array)
                    {
                        sb.Append('[');
                        var items = (List<object>)Values[i];
                        if (items.Count > 0)
                        {
                            foreach (var item in items)
                            {
                                ValueStringify(field, item);
                                sb.Append(", ");
                            }
                            sb.Length -= 2;
                        }
                        sb.AppendLine("];");
                    }
                    else
                    {
                        ValueStringify(field, Values[i]);
                        sb.AppendLine(";");
                    }
                }
            }
            sb.AppendIndent(indent);
            sb.Append('}');
        }

        public string Stringify(IList<RszInstance>? instances = null, int indent = 0)
        {
            StringBuilder sb = new();
            Stringify(sb, instances, indent);
            return sb.ToString();
        }

        /// <summary>
        /// 创建RszInstance
        /// </summary>
        /// <param name="rszParser">RszParser</param>
        /// <param name="rszClass">Rsz类型</param>
        /// <param name="index">instance序号</param>
        /// <param name="createChildren">创建子对象</param>
        /// <returns></returns>
        public static RszInstance CreateInstance(RszParser rszParser, RszClass rszClass, int index = -1, bool createChildren = true)
        {
            RszInstance instance = new(rszClass, index);
            var fields = instance.Fields;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.array)
                {
                    instance.Values[i] = new List<object>();
                }
                else if (field.IsReference)
                {
                    if (createChildren)
                    {
                        RszClass? fieldClass = rszParser.GetRSZClass(field.original_type) ??
                            throw new Exception($"RszClass {field.original_type} not found!");
                        instance.Values[i] = CreateInstance(rszParser, fieldClass, -1, createChildren) ??
                            throw new NullReferenceException($"Can not create RszInstance of type {fieldClass.name}");
                    }
                }
                else
                {
                    instance.Values[i] = CreateNormalObject(field);
                }
            }
            return instance;
        }

        /// <summary>
        /// 创建数组元素
        /// </summary>
        /// <param name="rszParser"></param>
        /// <param name="field"></param>
        /// <param name="className"></param>
        /// <returns></returns>
        public static object? CreateArrayItem(RszParser rszParser, RszField field, string? className = null)
        {
            if (field.type == RszFieldType.Object)
            {
                className ??= GetElementType(field.original_type);
                var rszClass = rszParser.GetRSZClass(className) ??
                    throw new Exception($"RszClass {className} not found!");
                return CreateInstance(rszParser, rszClass);
            }
            else
            {
                return CreateNormalObject(field);
            }
        }

        /// <summary>
        /// 创建普通对象
        /// </summary>
        /// <param name="field"></param>
        /// <returns></returns>
        public static object CreateNormalObject(RszField field)
        {
            if (field.type == RszFieldType.Data)
            {
                return new byte[field.size];
            }
            else if (field.IsString)
            {
                return "";
            }
            var type = RszFieldTypeToCSharpType(field.type);
            return Activator.CreateInstance(type) ??
                throw new NullReferenceException($"Can not create instance of type {type.Name}");
        }

        /// <summary>
        /// 根据数组类型获取元素类型
        /// </summary>
        /// <param name="arrayType"></param>
        /// <returns></returns>
        public static string GetElementType(string arrayType)
        {
            if (arrayType.EndsWith("[]"))
            {
                return arrayType[..^2];
            }
            const string listPrefix = "System.Collections.Generic.List`1<";
            if (arrayType.StartsWith(listPrefix))
            {
                return arrayType[listPrefix.Length..-1];
            }
            return arrayType;
        }
    }
}
