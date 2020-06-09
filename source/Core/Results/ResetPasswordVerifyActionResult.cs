using IdentityServer3.Core.Models;
using IdentityServer3.Core.Services;
using IdentityServer3.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.Results
{
    internal class ResetPasswordVerifyActionResult : HtmlStreamActionResult
    {
        public ResetPasswordVerifyActionResult(IViewService viewSvc, ResetPasswordViewModel model, SignInMessage message)
            : base(async () => await viewSvc.ResetPasswordVerify(model, message))
        {
            if (viewSvc == null) throw new ArgumentNullException("viewSvc");
            if (model == null) throw new ArgumentNullException("model");
            if (message == null) throw new ArgumentNullException("message");
        }
    }
}
