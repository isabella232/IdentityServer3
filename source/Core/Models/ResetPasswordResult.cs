using IdentityServer3.Core.Results;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.Models
{
    public class ResetPasswordResult
    {
        /// <summary>
        /// Indicates whether the result is error result.
        /// </summary>
        public bool IsError { get; private set; }

        /// <summary>
        /// Reset password result error message.
        /// </summary>
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// Initializes successful result.
        /// </summary>
        public ResetPasswordResult()
        {
            this.IsError = false;
        }

        public ResetPasswordResult(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
                throw new ArgumentNullException("errorMessage");

            this.IsError = true;
            this.ErrorMessage = errorMessage;
        }
    }
}
