using IdentityServer3.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.Services.Default
{
    public class DefaultPasswordResetService : IPasswordResetService
    {
        /// <summary>
        /// This method is called when the user asks for password to be reset.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public virtual Task ResetPasswordAsync(ResetPasswordContext context)
        {
            return Task.FromResult(0);
        }

        /// <summary>
        /// This method is called when the user provides new password.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public virtual Task ResetPasswordAsync(ResetPasswordCallbackContext context)
        {
            return Task.FromResult(0);
        }

        /// <summary>
        /// This method is called to verify user using passcode.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public virtual Task ResetPasswordVerifyAsync(ResetPasswordVerifyContext context)
        {
            return Task.FromResult(0);
        }

        /// <summary>
        /// This method is called when user's password needs to be changed based on login attempt.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public virtual Task HandlePasswordChangeForcedAsync(LocalAuthenticationContext context)
        {
            return Task.FromResult(0);
        }
    }
}
