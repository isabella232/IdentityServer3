using Autofac;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.Configuration
{
    public class AutofacResolver
    {
        IContainer container;

        internal AutofacResolver(IContainer container)
        {
            this.container = container;
        }

        public T Resolve<T>()
        {
            return this.container.Resolve<T>();
        }
    }
}
