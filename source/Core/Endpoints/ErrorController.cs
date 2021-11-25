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
using IdentityServer3.Core.Extensions;
using IdentityServer3.Core.Logging;
using IdentityServer3.Core.Results;
using IdentityServer3.Core.Services;
using IdentityServer3.Core.ViewModels;
using System.Net.Http;
using System.Web.Http;

namespace IdentityServer3.Core.Endpoints
{
    internal class ErrorController : ApiController
    {
        private readonly IViewService viewService;
        private readonly IdentityServerOptions options;
        private readonly ILocalizationService localizationService;

        private readonly static ILog Logger = LogProvider.GetCurrentClassLogger();

        public ErrorController(IViewService viewService, ILocalizationService localizationService, IdentityServerOptions options)
        {
            this.viewService = viewService;
            this.localizationService = localizationService;
            this.options = options;
        }

        [HttpGet]
        public IHttpActionResult Get(string message)
        {
            Logger.Info("Error page requested - rendering");

            var context = Request.GetOwinContext();
            var errorModel = new ErrorViewModel
            {
                RequestId = context.GetRequestId(),
                SiteName = this.options.SiteName,
                SiteUrl = context.GetIdentityServerBaseUrl(),
                CurrentUrl = context.Request.Uri.AbsoluteUri,
                ErrorMessage = this.localizationService.GetMessage(message),
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = context.GetIdentityServerLogoutUrl(),
            };

            var errorResult = new ErrorActionResult(this.viewService, errorModel);
            return errorResult;
        }
    }
}