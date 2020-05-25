using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.ViewModels
{
    public class ResetPasswordViewModel : LoginBaseViewModel
    {
        /// <summary>
        /// The URL to POST reset password request to. Will be <c>null</c> if local login is disabled.
        /// <see cref="LoginCredentials"/> for the model for the submitted data.
        /// </summary>
        /// <value>
        /// The reset password URL.
        /// </value>
        public string ResetPasswordUrl { get; set; }

        /// <summary>
        /// Indicates that the password reset request was successful.
        /// </summary>
        public bool? IsSuccessfulReset { get; set; }
    }
}
