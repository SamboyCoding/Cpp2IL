using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis
{
    public static class FieldUtils
    {
        private static List<FieldInType> RecalculateFieldOffsetsForGenericType(TypeReference type)
        {
            var baseType = type.Resolve();
            var ret = new List<FieldInType>();
            
            //Initialize to either 0, 0x8, or 0x10
            var offset = type.IsValueType ? 0UL : (ulong) (Utils.GetPointerSizeBytes() * 2);
            foreach (var field in baseType.Fields.Where(f => !f.IsStatic))
            {
                var fieldType = field.FieldType!;
                if (fieldType is GenericParameter gp)
                    fieldType = GenericInstanceUtils.ResolveGenericParameterType(gp, type) ?? fieldType;
                
                ret.Add(new FieldInType
                {
                    Name = field.Name,
                    DeclaringType = field.DeclaringType,
                    FieldType = fieldType,
                    Static = false,
                    Offset = offset
                });

                offset += Utils.GetSizeOfObject(fieldType);
            }

            return ret;
        }
        
        public static FieldBeingAccessedData? GetFieldBeingAccessed(TypeReference onWhat, ulong offset, bool tryFindFloatingPointValue)
        {
            var typeDef = onWhat.Resolve();

            if (typeDef == null) return null;

            var fields = SharedState.FieldsByType[typeDef].ToList();

            var thisType = typeDef;
            while (thisType.BaseType != null)
            {
                thisType = thisType.BaseType.Resolve();
                fields.AddRange(SharedState.FieldsByType[thisType]);
            }

            fields = fields.Where(f => !f.Static && f.Constant == null).ToList();

            // if (onWhat is TypeDefinition {HasGenericParameters: true})
            //     onWhat = onWhat.MakeGenericInstanceType(Utils.ObjectReference.Repeat(onWhat.GenericParameters.Count).Cast<TypeReference>().ToArray());

            if (onWhat is GenericInstanceType git || onWhat.HasGenericParameters)
                fields = RecalculateFieldOffsetsForGenericType(onWhat);

            if (fields == null) return null;

            var fieldInType = fields.FirstOrDefault(f => f.Offset == offset);

            if (fieldInType.Offset != offset && GetIndirectlyPointedAtField(fields, offset, tryFindFloatingPointValue) is { } fieldBeingAccessedData)
            {
                return fieldBeingAccessedData;
            }

            if (tryFindFloatingPointValue && fieldInType.FieldType?.IsValueType == true && !fieldInType.FieldType.ShouldBeInFloatingPointRegister())
            {
                var potentialResult = GetIndirectlyPointedAtField(fields, offset, tryFindFloatingPointValue);
                if (potentialResult != null)
                    return potentialResult;
            }

            if (fieldInType.Name == null!) return null; //The "default" part of "FirstOrDefault"

            var field = fieldInType.DeclaringType.Fields.FirstOrDefault(f => f.Name == fieldInType.Name);

            if (field == null) return null;

            return FieldBeingAccessedData.FromDirectField(field);
        }

        private static FieldBeingAccessedData? GetIndirectlyPointedAtField(List<FieldInType> allFields, ulong offset, bool tryFindFloatingPointValue)
        {
            //We have no field directly at this offset - find the one immediately prior, and map that struct to its own fields
            var structFIT = allFields.FindLast(f => !f.Static && f.Offset <= offset);

            if (structFIT.FieldType == null || structFIT.Constant != null) return null; //Couldn't find one, or they're all constants

            if (!structFIT.FieldType.IsValueType || structFIT.FieldType.IsPrimitive) return null; //Not a struct

            var structType = structFIT.FieldType.Resolve();

            if (structType == null) return null;

            if (structFIT.Offset == 0 && structFIT.FieldType == structFIT.DeclaringType)
                throw new Exception($"Self-referencing field? Field {structFIT.Name} in {structFIT.DeclaringType} is at offset 0 and is of the same type as that which declares it. Bailing out to avoid stack overflow.");

            offset -= structFIT.Offset;

            if (structFIT.ResolveToFieldDef() == null)
                //This was gonna break anyway, give some context
                throw new Exception($"GetFieldInType: Encountered a field with a null def? {structFIT.Name} of type {structFIT.FieldType} in {structFIT.DeclaringType} has null FieldDef.");

            var nextLink = GetFieldBeingAccessed(structType, offset, tryFindFloatingPointValue);
            if(nextLink != null)
                return FieldBeingAccessedData.FromImpliedLoad(structFIT.ResolveToFieldDef(), nextLink);

            return null;
        }

        /// <summary>
        /// Represents the access of a field in a type
        /// If FinalLoadInChain is non-null, then ImpliedFieldLoad and NextChainLink will be null, and vice-versa.
        /// Essentially, this is for representing direct il2cpp struct offset accesses where, instead of loading the struct, and then the field inside the sturct,
        /// it just loads that field directly - so we represent as an ImpliedFieldLoad of the struct, containing a NextChainLink which contains only
        /// a FinalLoadInChain of the actual field.
        ///
        /// I hope that makes sense.
        /// </summary>
        public class FieldBeingAccessedData
        {
            public readonly FieldDefinition? ImpliedFieldLoad;
            public readonly FieldDefinition? FinalLoadInChain;
            public readonly FieldBeingAccessedData? NextChainLink;

            private FieldBeingAccessedData(FieldDefinition? impliedFieldLoad, FieldDefinition? finalLoadInChain, FieldBeingAccessedData? nextChainLink)
            {
                ImpliedFieldLoad = impliedFieldLoad;
                FinalLoadInChain = finalLoadInChain;
                NextChainLink = nextChainLink;
            }

            public static FieldBeingAccessedData FromDirectField(FieldDefinition directLoad)
            {
                return new FieldBeingAccessedData(null, directLoad, null);
            }

            public static FieldBeingAccessedData FromImpliedLoad(FieldDefinition? impliedLoad, FieldBeingAccessedData? nextLink)
            {
                if (impliedLoad == null)
                    throw new ArgumentException($"Cannot implicitly load null field before accessing {nextLink}");

                return new FieldBeingAccessedData(impliedLoad, null, nextLink);
            }

            public override string ToString()
            {
                if (FinalLoadInChain != null)
                    return FinalLoadInChain.Name;

                return $"{ImpliedFieldLoad!.Name}.{NextChainLink}";
            }

            public List<Instruction> GetILToLoad(ILProcessor processor)
            {
                if (NextChainLink != null)
                {
                    var ret = new List<Instruction>
                    {
                        processor.Create(OpCodes.Ldfld, processor.ImportReference(ImpliedFieldLoad!))
                    };
                    
                    ret.AddRange(NextChainLink.GetILToLoad(processor));
                    return ret;
                }

                if (FinalLoadInChain == null)
                    throw new TaintedInstructionException("FinalLoadInChain is null");

                return new List<Instruction>
                {
                    processor.Create(OpCodes.Ldfld, processor.ImportReference(FinalLoadInChain))
                };
            }

            public TypeReference? GetFinalType()
            {
                return GetLast().FinalLoadInChain!.FieldType;
            }

            public FieldBeingAccessedData GetLast()
            {
                if (NextChainLink != null)
                    return NextChainLink.GetLast();

                return this;
            }

            protected bool Equals(FieldBeingAccessedData other)
            {
                return Equals(ImpliedFieldLoad, other.ImpliedFieldLoad) && Equals(FinalLoadInChain, other.FinalLoadInChain) && Equals(NextChainLink, other.NextChainLink);
            }

            public override bool Equals(object? obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((FieldBeingAccessedData) obj);
            }

            // public override int GetHashCode()
            // {
            //     return HashCode.Combine(ImpliedFieldLoad, FinalLoadInChain, NextChainLink);
            // }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (ImpliedFieldLoad != null ? ImpliedFieldLoad.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (FinalLoadInChain != null ? FinalLoadInChain.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (NextChainLink != null ? NextChainLink.GetHashCode() : 0);
                    return hashCode;
                }
            }

            public static bool operator ==(FieldBeingAccessedData? left, FieldBeingAccessedData? right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(FieldBeingAccessedData? left, FieldBeingAccessedData? right)
            {
                return !Equals(left, right);
            }
        }

        public static FieldDefinition? GetStaticFieldByOffset(StaticFieldsPtr fieldsPtr, uint fieldOffset)
        {
            var type = fieldsPtr.TypeTheseFieldsAreFor.Resolve();

            if (type == null) return null;

            var theFields = SharedState.FieldsByType[type];
            string fieldName;
            try
            {
                fieldName = theFields.SingleOrDefault(f => f.Static && f.Constant == null && f.Offset == fieldOffset).Name;
            }
            catch (InvalidOperationException)
            {
                var matchingFields = theFields.Where(f => f.Static && f.Constant == null && f.Offset == fieldOffset).ToList();
                Logger.ErrorNewline($"FieldUtils#GetStaticFieldByOffset: More than one static field at offset 0x{fieldOffset:X} in type {type}! Matches: " + matchingFields.Select(f => f.Name).ToStringEnumerable());
                return null;
            }

            if (string.IsNullOrEmpty(fieldName)) return null;

            return type.Fields.FirstOrDefault(f => f.IsStatic && f.Name == fieldName);
        }
    }
}