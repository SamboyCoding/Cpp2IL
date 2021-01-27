using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;

namespace Cpp2IL.Analysis
{
    public static class FieldUtils
    {
        public static FieldBeingAccessedData? GetFieldBeingAccessed(TypeReference onWhat, ulong offset, bool tryFindFloatingPointValue)
        {
            var typeDef = onWhat.Resolve();

            if (typeDef == null) return null;

            var fields = SharedState.FieldsByType[typeDef];

            if (fields == null) return null;

            var fieldInType = fields.FirstOrDefault(f => f.Offset == offset);

            if (fieldInType.Offset != offset && GetIndirectlyPointedAtField(fields, offset, tryFindFloatingPointValue) is { } fieldBeingAccessedData)
            {
                return fieldBeingAccessedData;
            }

            if (tryFindFloatingPointValue && fieldInType.FieldType?.IsValueType == true && !Utils.ShouldBeInFloatingPointRegister(fieldInType.FieldType))
            {
                var potentialResult = GetIndirectlyPointedAtField(fields, offset, tryFindFloatingPointValue);
                if (potentialResult != null)
                    return potentialResult;
            }

            if (fieldInType.Offset != offset) return null; //The "default" part of "FirstOrDefault"

            var field = typeDef.Fields.FirstOrDefault(f => f.Name == fieldInType.Name);

            if (field == null) return null;

            return FieldBeingAccessedData.FromDirectField(field);
        }

        private static FieldBeingAccessedData? GetIndirectlyPointedAtField(List<FieldInType> allFields, ulong offset, bool tryFindFloatingPointValue)
        {
            //We have no field directly at this offset - find the one immediately prior, and map that struct to its own fields
            var structFIT = allFields.FindLast(f => f.Offset <= offset);

            if (structFIT.Name == null || structFIT.Constant != null) return null; //Couldn't find one, or they're all constants

            if (!structFIT.FieldType.IsValueType || structFIT.FieldType.IsPrimitive) return null; //Not a struct

            var structType = structFIT.FieldType.Resolve();

            if (structType == null) return null;

            offset -= structFIT.Offset;

            var data = new FieldBeingAccessedData
            {
                ImpliedFieldLoad = structFIT.ResolveToFieldDef(),
                FinalLoadInChain = null,
            };

            var subAccess = GetFieldBeingAccessed(structType, offset, tryFindFloatingPointValue);

            if (subAccess == null) return null;
            
            data.NextChainLink = subAccess;

            return data;
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
            public FieldDefinition? ImpliedFieldLoad;
            public FieldDefinition? FinalLoadInChain;
            public FieldBeingAccessedData? NextChainLink;

            public static FieldBeingAccessedData FromDirectField(FieldDefinition directLoad)
            {
                return new FieldBeingAccessedData
                {
                    FinalLoadInChain = directLoad
                };
            }

            public override string ToString()
            {
                if (FinalLoadInChain != null)
                    return FinalLoadInChain.Name;

                return $"{ImpliedFieldLoad!.Name}.{NextChainLink}";
            }
            
            public TypeReference GetFinalType()
            {
                return FinalLoadInChain?.FieldType ?? NextChainLink!.GetFinalType();
            }
        }
    }
}