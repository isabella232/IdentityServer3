using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.ViewModels
{
    public class Login
    {
        /// <summary>
        /// Gets or sets the username.
        /// </summary>
        /// <value>
        /// The username.
        /// </value>
        public string Username { get; set; }

        /// <summary>
        /// Indicates that the password reset request was successful.
        /// </summary>
        public bool IsSuccessfulReset { get; set; }
    }
}
