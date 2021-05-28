using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace MethodBoundaryAspect.Fody.Ordering
{
    public class AspectInfo
    {
        private const string AspectCanTHaveRoleAndBeOrderedBeforeOrAfterThatRole =
            "Aspect '{0}' can't have role '{1}' and be ordered before or after that role";

        private const string AspectHasToProvideANonEmptyMethodBoundaryAspectAttributesProvideAspectRoleAttribute =
            "Aspect '{0}' has to provide a non-empty MethodBoundaryAspect.Attributes.ProvideAspectRoleAttribute attribute";

        private const string AspectHasMultipleOrderIndicesDefinedOnTheSameLevel =
            "Aspect '{0}' has multiple order indices defined on {1} level";

        public AspectInfo(CustomAttribute aspectAttribute)
        {
            AspectAttribute = aspectAttribute;
            Name = aspectAttribute.AttributeType.FullName;
            AspectTypeDefinition = aspectAttribute.AttributeType.Resolve();

            var aspectAttributes = AspectTypeDefinition.CustomAttributes;
            InitRole(aspectAttributes);
            InitOrder(aspectAttributes);
            InitSkipProperties(aspectAttributes);
            InitTargetMembers();
            InitTargetTypes();
            InitTargetInterfaces();
            InitTargetTypeOrMethodAttributes();
            InitChangingInputArguments(aspectAttributes);
        }

        public TypeDefinition AspectTypeDefinition { get; }

        public string Name { get; private set; }

        public string Role { get; private set; }

        public bool SkipProperties { get; private set; }

        public CustomAttribute AspectAttribute { get; private set; }

        public List<CustomAttribute> AspectRoleDependencyAttributes { get; private set; }

#pragma warning disable 0618
        public AspectOrder Order { get; private set; }
#pragma warning restore 0618

        public int? OrderIndex { get; private set; }

        public bool AllowChangingInputArguments { get; private set; }

        public IEnumerable<MethodAttributes> AttributeTargetMemberAttributes { get; set; } =
            new List<MethodAttributes>
            {
                MethodAttributes.Private,
                MethodAttributes.FamANDAssem,
                MethodAttributes.Assembly,
                MethodAttributes.Family,
                MethodAttributes.FamORAssem,
                MethodAttributes.Public
            };

        public IEnumerable<string> AttributeTargetTypes { get; set; } = new List<string>();

        public IEnumerable<string> AttributeTargetInterfaces { get; set; } = new List<string>();

        public IEnumerable<string> AttributeTargetTypeOrMethodAttributes { get; set; } = new List<string>();

        public bool HasTargetMemberAttribute(MethodAttributes visibility) =>
            AttributeTargetMemberAttributes.Contains(visibility);

        public bool HasTargetTypeInterfaceOrAttribute(MethodDefinition method)
        {
            if ((AttributeTargetTypes == null || !AttributeTargetTypes.Any())
                && (AttributeTargetInterfaces == null || !AttributeTargetInterfaces.Any())
                && (AttributeTargetTypeOrMethodAttributes == null || !AttributeTargetTypeOrMethodAttributes.Any()))
            {
                return true;
            }

            bool result = false;

            if (AttributeTargetTypes != null && AttributeTargetTypes.Any())
            {
                result = AttributeTargetTypes.Contains(method.DeclaringType.FullName);
            }

            if (!result && AttributeTargetInterfaces != null && AttributeTargetInterfaces.Any())
            {
                bool hasInterface = method.DeclaringType.Interfaces.Any(i => AttributeTargetInterfaces.Contains(i.InterfaceType.Resolve().FullName));

                TypeDefinition baseType = method.DeclaringType.BaseType?.Resolve();

                while (!hasInterface && baseType != null)
                {
                    hasInterface = baseType.Interfaces != null && baseType.Interfaces.Any(i => AttributeTargetInterfaces.Contains(i.InterfaceType.Resolve().FullName));
                    baseType = method.DeclaringType.BaseType?.Resolve();
                }

                result = hasInterface;
            }

            if (!result && AttributeTargetTypeOrMethodAttributes != null && AttributeTargetTypeOrMethodAttributes.Any())
            {
                result = method.CustomAttributes.Any(a => AttributeTargetTypeOrMethodAttributes.Contains(a.AttributeType.FullName))
                    || method.DeclaringType.CustomAttributes.Any(a => AttributeTargetTypeOrMethodAttributes.Contains(a.AttributeType.FullName));
            }

            return result;
        }

        private void InitRole(IEnumerable<CustomAttribute> aspectAttributes)
        {
            Role = "<Default>";

            var roleAttribute = aspectAttributes
                .SingleOrDefault(c => c.AttributeType.FullName == AttributeFullNames.ProvideAspectRoleAttribute);

            if (roleAttribute == null)
                return;

            var role = (string)roleAttribute.ConstructorArguments[0].Value;
            if (string.IsNullOrEmpty(role))
            {
                var msg =
                    string.Format(AspectHasToProvideANonEmptyMethodBoundaryAspectAttributesProvideAspectRoleAttribute,
                        Name);
                throw new InvalidAspectConfigurationException(msg);
            }

            Role = role;
        }

        private void InitOrder(IEnumerable<CustomAttribute> aspectAttributes)
        {
            AspectRoleDependencyAttributes =
                aspectAttributes.Where(
                    c => c.AttributeType.FullName == AttributeFullNames.AspectRoleDependencyAttribute).ToList();

            if (AspectRoleDependencyAttributes.Count == 0)
                return;

#pragma warning disable 0618
            var aspectOrder = new AspectOrder(this);
#pragma warning restore 0618

            foreach (var roleDependencyAttribute in AspectRoleDependencyAttributes)
            {
                var role = (string)roleDependencyAttribute.ConstructorArguments[2].Value;
                if (role == Role)
                {
                    var msg = string.Format(AspectCanTHaveRoleAndBeOrderedBeforeOrAfterThatRole, Name, role);
                    throw new InvalidAspectConfigurationException(msg);
                }

                var position = (int)roleDependencyAttribute.ConstructorArguments[1].Value;

                aspectOrder.AddRole(role, position);
            }

            Order = aspectOrder;
        }

        private void InitSkipProperties(IEnumerable<CustomAttribute> aspectAttributes)
        {
            var skipPropertiesAttribute = aspectAttributes
                .SingleOrDefault(c => c.AttributeType.FullName == AttributeFullNames.AspectSkipPropertiesAttribute);

            if (skipPropertiesAttribute == null)
                return;

            var skipProperties = (bool)skipPropertiesAttribute.ConstructorArguments[0].Value;

            SkipProperties = skipProperties;
        }

        public void InitOrderIndex(
            IEnumerable<CustomAttribute> assemblyAspectAttributes,
            IEnumerable<CustomAttribute> classAspectAttributes,
            IEnumerable<CustomAttribute> methodAspectAttributes)
        {
            InternalInitOrderIndex(assemblyAspectAttributes, "assembly");
            InternalInitOrderIndex(classAspectAttributes, "class");
            InternalInitOrderIndex(methodAspectAttributes, "method");
        }

        private void InternalInitOrderIndex(IEnumerable<CustomAttribute> aspectAttributes, string level)
        {
            var orderIndexAttributes = aspectAttributes
                    .Where(c => c.AttributeType.FullName == AttributeFullNames.AspectOrderIndexAttribute &&
                                ((TypeReference)c.ConstructorArguments[0].Value).FullName == AspectTypeDefinition.FullName)
                    .ToList();

            if (orderIndexAttributes.Count > 1)
                throw new InvalidAspectConfigurationException(string.Format(AspectHasMultipleOrderIndicesDefinedOnTheSameLevel, Name, level));

            if (orderIndexAttributes.Count == 1)
                OrderIndex = (int)orderIndexAttributes[0].ConstructorArguments[1].Value;
        }

        private void InitTargetMembers()
        {
            var targetMembersAttribute = AspectAttribute.Properties
                .FirstOrDefault(property => property.Name == AttributeNames.AttributeTargetMemberAttributes);

            if (targetMembersAttribute.Equals(default(CustomAttributeNamedArgument)))
            {
                return;
            }

            var memberAttributes = new List<MethodAttributes>();

            var attributes = (MulticastAttributes)targetMembersAttribute.Argument.Value;
            if (attributes.HasFlag(MulticastAttributes.Private))
            {
                memberAttributes.Add(MethodAttributes.Private);
            }
            if (attributes.HasFlag(MulticastAttributes.Protected))
            {
                memberAttributes.Add(MethodAttributes.Family);
            }
            if (attributes.HasFlag(MulticastAttributes.Internal))
            {
                memberAttributes.Add(MethodAttributes.Assembly);
            }
            if (attributes.HasFlag(MulticastAttributes.InternalAndProtected))
            {
                memberAttributes.Add(MethodAttributes.FamANDAssem);
            }
            if (attributes.HasFlag(MulticastAttributes.InternalOrProtected))
            {
                memberAttributes.Add(MethodAttributes.FamORAssem);
            }
            if (attributes.HasFlag(MulticastAttributes.Public))
            {
                memberAttributes.Add(MethodAttributes.Public);
            }

            AttributeTargetMemberAttributes = memberAttributes;
        }

        private void InitChangingInputArguments(IEnumerable<CustomAttribute> aspectAttributes)
        {
            AllowChangingInputArguments = aspectAttributes
                .Any(c => c.AttributeType.FullName == AttributeFullNames.AllowChangingInputArguments);

        }

        private void InitTargetTypes()
        {
            var targetTypesProperty = AspectAttribute.Properties
                .FirstOrDefault(property => property.Name == AttributeNames.AttributeTargetTypes);

            if (targetTypesProperty.Equals(default(CustomAttributeNamedArgument)))
            {
                return;
            }

            string targetTypesString = (string)targetTypesProperty.Argument.Value;

            if (!string.IsNullOrEmpty(targetTypesString))
            {
                AttributeTargetTypes = targetTypesString
                    .Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToArray();
            }
        }

        private void InitTargetInterfaces()
        {
            var targetInterfacesProperty = AspectAttribute.Properties
                   .FirstOrDefault(property => property.Name == AttributeNames.AttributeTargetInterfaces);

            if (targetInterfacesProperty.Equals(default(CustomAttributeNamedArgument)))
            {
                return;
            }

            string targetInterfacesString = (string)targetInterfacesProperty.Argument.Value;

            if (!string.IsNullOrEmpty(targetInterfacesString))
            {
                AttributeTargetInterfaces = targetInterfacesString
                    .Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToArray();
            }
        }

        private void InitTargetTypeOrMethodAttributes()
        {
            var targetTypeOrMethodAttributesProperty = AspectAttribute.Properties
                   .FirstOrDefault(property => property.Name == AttributeNames.AttributeTargetTypeOrMethodAttributes);

            if (targetTypeOrMethodAttributesProperty.Equals(default(CustomAttributeNamedArgument)))
            {
                return;
            }

            string targetTypeOrMethodAttributesString = (string)targetTypeOrMethodAttributesProperty.Argument.Value;

            if (!string.IsNullOrEmpty(targetTypeOrMethodAttributesString))
            {
                AttributeTargetTypeOrMethodAttributes = targetTypeOrMethodAttributesString
                    .Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToArray();
            }
        }
    }
}