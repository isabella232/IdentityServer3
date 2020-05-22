using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer3.Core.ViewModels
{
    public class LoginBaseViewModel : ErrorViewModel
    {
        /// <summary>
        /// The anti forgery values.
        /// </summary>
        /// <value>
        /// The anti forgery.
        /// </value>
        public AntiForgeryTokenViewModel AntiForgery { get; set; }

        /// <summary>
        /// The display name of the client.
        /// </summary>
        /// <value>
        /// The name of the client.
        /// </value>
        public string ClientName { get; set; }

        /// <summary>
        /// The URL for more information about the client.
        /// </summary>
        /// <value>
        /// The client URL.
        /// </value>
        public string ClientUrl { get; set; }

        /// <summary>
        /// The URL for the client's logo image.
        /// </summary>
        /// <value>
        /// The client logo URL.
        /// </value>
        public string ClientLogoUrl { get; set; }

        /// <summary>
        /// The value to populate the username field.
        /// </summary>
        /// <value>
        /// The username.
        /// </value>
        public string Username { get; set; }
    }
}
