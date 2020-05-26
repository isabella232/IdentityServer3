using IdentityServer3.Core.Services;
using IdentityServer3.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.Results
{
    internal class ResetPasswordCallbackActionResult : HtmlStreamActionResult
    {
        public ResetPasswordCallbackActionResult(IViewService viewSvc, ResetPasswordViewModel model, string token)
            : base(async () => await viewSvc.ResetPasswordCallback(model, token))
        {
            if (viewSvc == null) throw new ArgumentNullException("viewSvc");
            if (model == null) throw new ArgumentNullException("model");
            if (token == null) throw new ArgumentNullException("token");
        }
    }
}
