using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;

namespace Cpp2IL
{
    internal static class CecilExtensions
    {
        /// <summary>
        /// Is childTypeDef a subclass of parentTypeDef. Does not test interface inheritance
        /// </summary>
        /// <param name="childTypeDef"></param>
        /// <param name="parentTypeDef"></param>
        /// <returns></returns>
        public static bool IsSubclassOf(this TypeReference childTypeDef, TypeReference parentTypeDef) =>
            childTypeDef.MetadataToken
            != parentTypeDef.MetadataToken
            && childTypeDef
                .EnumerateBaseClasses()
                .Any(b => b.MetadataToken == parentTypeDef.MetadataToken);

        /// <summary>
        /// Does childType inherit from parentInterface
        /// </summary>
        /// <param name="childType"></param>
        /// <param name="parentInterfaceDef"></param>
        /// <returns></returns>
        public static bool DoesAnySubTypeImplementInterface(this TypeReference childType, TypeReference parentInterfaceDef)
        {
            return childType
                .EnumerateBaseClasses()
                .Any(typeDefinition => typeDefinition.DoesSpecificTypeImplementInterface(parentInterfaceDef));
        }

        /// <summary>
        /// Does the childType directly inherit from parentInterface. Base
        /// classes of childType are not tested
        /// </summary>
        /// <param name="childTypeDef"></param>
        /// <param name="parentInterfaceDef"></param>
        /// <returns></returns>
        public static bool DoesSpecificTypeImplementInterface(this TypeReference childTypeDef, TypeReference parentInterfaceDef)
        {
            return childTypeDef
                .Resolve()?
                .Interfaces
                .Any(ifaceDef => DoesSpecificInterfaceImplementInterface(ifaceDef.InterfaceType.Resolve(), parentInterfaceDef)) ?? false;
        }

        /// <summary>
        /// Does interface iface0 equal or implement interface iface1
        /// </summary>
        /// <param name="iface0"></param>
        /// <param name="iface1"></param>
        /// <returns></returns>
        public static bool DoesSpecificInterfaceImplementInterface(TypeReference iface0, TypeReference iface1)
        {
            return iface0.MetadataToken == iface1.MetadataToken || iface0.DoesAnySubTypeImplementInterface(iface1);
        }

        /// <summary>
        /// Is source type assignable to target type
        /// </summary>
        /// <param name="target"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        public static bool IsAssignableFrom(this TypeDefinition target, TypeReference source)
            => target == source
               || target.MetadataToken == source.MetadataToken
               || source.IsSubclassOf(target)
               || target.IsInterface && source.DoesAnySubTypeImplementInterface(target);

        /// <summary>
        /// Enumerate the current type, it's parent and all the way to the top type
        /// </summary>
        /// <param name="klassType"></param>
        /// <returns></returns>
        public static IEnumerable<TypeReference> EnumerateBaseClasses(this TypeReference klassType)
        {
            for (var typeDefinition = klassType; typeDefinition != null; typeDefinition = typeDefinition.Resolve()?.BaseType?.Resolve())
            {
                yield return typeDefinition;
            }
        }
    }
}