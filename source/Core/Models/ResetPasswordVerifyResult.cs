using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.Models
{
    public class ResetPasswordVerifyResult : ResetPasswordResult
    {
        /// <summary>
        /// Gets or sets the token for password reset.
        /// </summary>
        public string Token { get; set; }

        public ResetPasswordVerifyResult()
            : base() { }

        public ResetPasswordVerifyResult(string errorMessage)
            : base(errorMessage) { }
    }
}
