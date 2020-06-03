/*
 * Copyright 2014, 2015 Dominick Baier, Brock Allen
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using IdentityServer3.Core.Configuration;
using System.Collections.Generic;

namespace IdentityServer3.Core.ViewModels
{
    /// <summary>
    /// Models that data needed to render the login page.
    /// </summary>
    public class LoginViewModel : LoginBaseViewModel
    {
        /// <summary>
        /// Indicates if "remember me" has been disabled and should not be displayed to the user.
        /// </summary>
        /// <value>
        ///   <c>true</c> if Remember Me is allowed; otherwise, <c>false</c>.
        /// </value>
        public bool AllowRememberMe { get; set; }

        /// <summary>
        /// The value to populate the "remember me" field.
        /// </summary>
        /// <value>
        ///   <c>true</c> if Remember Me is checked; otherwise, <c>false</c>.
        /// </value>
        public bool RememberMe { get; set; }

        /// <summary>
        /// Value indicating whether user name should be readonly.
        /// </summary>
        public bool UsernameReadonly { get; set; }

        /// <summary>
        /// List of external providers to display for home realm discover (HRD). 
        /// </summary>
        /// <value>
        /// The external providers.
        /// </value>
        public IEnumerable<LoginPageLink> ExternalProviders { get; set; }

        /// <summary>
        /// List of additional links configured to be displayed on the login page (e.g. as registration, or forgot password links).
        /// </summary>
        /// <value>
        /// The additional links.
        /// </value>
        public IEnumerable<LoginPageLink> AdditionalLinks { get; set; }
    }
}
