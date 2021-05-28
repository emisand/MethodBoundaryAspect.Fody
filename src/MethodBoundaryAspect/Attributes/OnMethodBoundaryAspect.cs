using System;
using System.Reflection;

namespace MethodBoundaryAspect.Fody.Attributes
{
    public abstract class OnMethodBoundaryAspect : Attribute
    {
        public string AttributeTargetInterfaces { get; set; }

        public MulticastAttributes AttributeTargetMemberAttributes { get; set; } =
                    MulticastAttributes.AnyVisibility;

        public string AttributeTargetTypeOrMethodAttributes { get; set; }

        public string AttributeTargetTypes { get; set; }

        public virtual void OnEntry(MethodExecutionArgs arg)
        {
        }

        public virtual void OnExit(MethodExecutionArgs arg)
        {
        }

        public virtual void OnException(MethodExecutionArgs arg)
        {
        }

        public virtual bool CompileTimeValidate(MethodBase method)
        {
            throw new NotImplementedException("TODO!");
        }
    }
}