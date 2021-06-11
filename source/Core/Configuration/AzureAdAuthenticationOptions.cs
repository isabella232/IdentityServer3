using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.Configuration
{
    public class AzureAdAuthenticationOptions
    {
        /// <summary>
        /// Gets or sets authentication type.
        /// </summary>
        public string SignInAsAuthenticationType { get; set; }

        /// <summary>
        /// Gets or sets Azure AD client ID.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Get or sets Azure AD tenant ID.
        /// </summary>
        public string TenantId { get; set; }

        /// <summary>
        /// Gets or sets the redirect URI.
        /// </summary>
        public string RedirectUri { get; set; }

        /// <summary>
        /// Gets or sets the callback URI.
        /// </summary>
        public string CallbackUri { get; set; }
    }
}
