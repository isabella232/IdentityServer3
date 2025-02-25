﻿/*
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

using IdentityModel;
using IdentityServer3.Core.Configuration;
using IdentityServer3.Core.Configuration.Hosting;
using IdentityServer3.Core.Events;
using IdentityServer3.Core.Extensions;
using IdentityServer3.Core.Logging;
using IdentityServer3.Core.Models;
using IdentityServer3.Core.Resources;
using IdentityServer3.Core.Results;
using IdentityServer3.Core.Services;
using IdentityServer3.Core.ViewModels;
using Microsoft.Owin;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;

namespace IdentityServer3.Core.Endpoints
{
    [ErrorPageFilter]
    [SecurityHeaders]
    [NoCache]
    [PreventUnsupportedRequestMediaTypes(allowFormUrlEncoded: true)]
    [HostAuthentication(Constants.PrimaryAuthenticationType)]
    internal class AuthenticationController : ApiController
    {
        public const int MaxSignInMessageLength = 100;

        private readonly static ILog Logger = LogProvider.GetCurrentClassLogger();

        private readonly IOwinContext context;
        private readonly IViewService viewService;
        private readonly IUserService userService;
        private readonly IPasswordResetService passwordResetService;
        private readonly IdentityServerOptions options;
        private readonly IClientStore clientStore;
        private readonly IEventService eventService;
        private readonly ILocalizationService localizationService;
        private readonly SessionCookie sessionCookie;
        private readonly MessageCookie<SignInMessage> signInMessageCookie;
        private readonly MessageCookie<SignOutMessage> signOutMessageCookie;
        private readonly LastUserNameCookie lastUserNameCookie;
        private readonly AntiForgeryToken antiForgeryToken;

        public AuthenticationController(
            OwinEnvironmentService owin,
            IViewService viewService,
            IUserService userService,
            IPasswordResetService passwordResetService,
            IdentityServerOptions idSvrOptions,
            IClientStore clientStore,
            IEventService eventService,
            ILocalizationService localizationService,
            SessionCookie sessionCookie,
            MessageCookie<SignInMessage> signInMessageCookie,
            MessageCookie<SignOutMessage> signOutMessageCookie,
            LastUserNameCookie lastUsernameCookie,
            AntiForgeryToken antiForgeryToken)
        {
            this.context = new OwinContext(owin.Environment);
            this.viewService = viewService;
            this.userService = userService;
            this.passwordResetService = passwordResetService;
            this.options = idSvrOptions;
            this.clientStore = clientStore;
            this.eventService = eventService;
            this.localizationService = localizationService;
            this.sessionCookie = sessionCookie;
            this.signInMessageCookie = signInMessageCookie;
            this.signOutMessageCookie = signOutMessageCookie;
            this.lastUserNameCookie = lastUsernameCookie;
            this.antiForgeryToken = antiForgeryToken;
        }

        [Route(Constants.RoutePaths.ResetPasswordCallback, Name = Constants.RouteNames.ResetPasswordCallback)]
        [HttpGet]
        public async Task<IHttpActionResult> ResetCallback(string token = null, string signin = null)
        {
            Logger.Info("Reset password callback page requested");

            if (token.IsMissing())
            {
                Logger.Info("No token passed");
                return RenderErrorPage();
            }

            Logger.DebugFormat("Token passed to reset password callback: {0}", token);

            SignInMessage signInMessage;
            IHttpActionResult errorResult;
            if (!this.ValidateSignin(signin, out errorResult, out signInMessage))
                return errorResult;

            return await RenderResetPasswordCallbackPage(token, signInMessageId: signin);
        }

        [Route(Constants.RoutePaths.ResetPasswordCallback)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IHttpActionResult> ResetCallback(string token, ResetCredentials model, string signin = null)
        {
            Logger.Info("Reset password callback page submitted");

            if (this.options.AuthenticationOptions.EnableLocalLogin == false)
            {
                Logger.Warn("EnableLocalLogin disabled -- returning 405 MethodNotAllowed");
                return StatusCode(HttpStatusCode.MethodNotAllowed);
            }

            if (token.IsMissing())
            {
                Logger.Info("No token passed");
                return RenderErrorPage();
            }

            SignInMessage signInMessage;
            IHttpActionResult errorResult;
            if (!this.ValidateSignin(signin, out errorResult, out signInMessage))
                return errorResult;

            if (model == null)
            {
                Logger.Error("No data submitted");
                return await RenderResetPasswordCallbackPage(token, localizationService.GetMessage(MessageIds.MissingToken), signInMessageId: signin);
            }

            var resetContext = new ResetPasswordCallbackContext { Token = token, Password = model.Password, ConfirmedPassword = model.ConfirmedPassword };
            await passwordResetService.ResetPasswordAsync(resetContext);

            var resetResult = resetContext.ResetPasswordResult;
            if (resetResult == null)
            {
                string error = "No result provided";

                Logger.WarnFormat("User service returned no result");

                await eventService.RaiseResetPasswordCallbackFailureEventAsync(error);

                return await RenderResetPasswordCallbackPage(token, error, signInMessageId: signin);
            }

            if (resetResult.IsError)
            {
                Logger.WarnFormat("User service returned invalid result: '{0}'", resetResult.ErrorMessage);

                await eventService.RaiseResetPasswordCallbackFailureEventAsync(resetResult.ErrorMessage);

                return await RenderResetPasswordCallbackPage(token, resetResult.ErrorMessage, signInMessageId: signin);
            }

            if (string.IsNullOrEmpty(resetContext.UserName))
            {
                Logger.WarnFormat("User service returned no username");

                string error = "No username provided";

                await eventService.RaiseResetPasswordCallbackFailureEventAsync(error);

                return await RenderResetPasswordCallbackPage(token, error, signInMessageId: signin);
            }

            Logger.InfoFormat("Reset password finished successfully for user '{0}'", resetContext.UserName);

            await eventService.RaiseResetPasswordCallbackSuccessEventAsync();

            return await LoginLocal(signin, new LoginCredentials() { Password = model.Password, Username = resetContext.UserName });
        }

        [Route(Constants.RoutePaths.ResetPasswordVerify, Name = Constants.RouteNames.ResetPasswordVerify)]
        [HttpGet]
        public async Task<IHttpActionResult> ResetVerify(string signin = null, string username = null)
        {
            Logger.Info("Reset password verify page requested");

            SignInMessage signInMessage;
            IHttpActionResult errorResult;
            if (!this.ValidateSignin(signin, out errorResult, out signInMessage))
                return errorResult;

            return await RenderResetPasswordVerifyPage(signInMessage, signin, username: username);
        }

        [Route(Constants.RoutePaths.ResetPasswordVerify)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IHttpActionResult> ResetVerify(string signin, LoginCredentials model)
        {
            Logger.Info("Reset password verify page requested");

            SignInMessage signInMessage;
            IHttpActionResult errorResult;
            if (!this.ValidateSignin(signin, out errorResult, out signInMessage))
                return errorResult;

            if (!(await IsLocalLoginAllowedForClient(signInMessage)))
            {
                Logger.ErrorFormat("Login not allowed for client {0}", signInMessage.ClientId);
                return RenderErrorPage();
            }

            if (model == null)
            {
                Logger.Error("No data submitted");
                return await RenderResetPasswordVerifyPage(signInMessage, signin, localizationService.GetMessage(MessageIds.InvalidUsernameOrPassword));
            }

            if (String.IsNullOrWhiteSpace(model.Username))
            {
                ModelState.AddModelError("Username", localizationService.GetMessage(MessageIds.UsernameRequired));
            }

            if (String.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.AddModelError("Password", localizationService.GetMessage(MessageIds.PasswordRequired));
            }

            if (!ModelState.IsValid)
            {
                Logger.Warn("Validation error: username or password missing");
                return await RenderResetPasswordVerifyPage(signInMessage, signin, ModelState.GetError(), model.Username);
            }

            if (model.Username.Length > options.InputLengthRestrictions.UserName || model.Password.Length > 6)
            {
                Logger.Error("Username or password submitted beyond allowed length");
                return await RenderResetPasswordVerifyPage(signInMessage, signin);
            }

            var authenticationContext = new ResetPasswordVerifyContext
            {
                UserName = model.Username.Trim(),
                Password = model.Password.Trim(),
                SignInMessage = signInMessage
            };

            await passwordResetService.ResetPasswordVerifyAsync(authenticationContext);

            var authResult = authenticationContext.ResetPasswordResult;
            if (authResult == null)
            {
                Logger.WarnFormat("User service indicated incorrect password: {0}", model.Password);

                var errorMessage = localizationService.GetMessage(MessageIds.InvalidUsernameOrPassword);
                await eventService.RaiseLocalLoginFailureEventAsync(model.Username, signin, signInMessage, errorMessage);

                return await RenderResetPasswordVerifyPage(signInMessage, signin, errorMessage, model.Username);
            }

            if (authResult.IsError)
            {
                Logger.WarnFormat("User service returned an error message: {0}", authResult.ErrorMessage);

                await eventService.RaiseResetPasswordVerifyFailureEventAsync(model.Username, signin, signInMessage, authResult.ErrorMessage);

                return await RenderResetPasswordVerifyPage(signInMessage, signin, authResult.ErrorMessage, model.Username);
            }

            if (string.IsNullOrEmpty(authResult.Token))
            {
                Logger.Warn("User service did not return token");
                return RenderErrorPage();
            }

            Logger.Info("Password successfully validated by user service");

            await eventService.RaiseResetPasswordVerifySuccessEventAsync(model.Username, signin, signInMessage);

            var url = Request.GetOwinContext().GetIdentityServerBaseUrl().EnsureTrailingSlash() +
                Constants.RoutePaths.ResetPasswordCallback + "?token=" + authResult.Token + "&signin=" + signin;

            return Redirect(url);
        }

        [Route(Constants.RoutePaths.ResetPassword, Name = Constants.RouteNames.ResetPassword)]
        [HttpGet]
        public async Task<IHttpActionResult> Reset(string signin = null)
        {
            Logger.Info("Reset password page requested");

            SignInMessage signInMessage;
            IHttpActionResult errorResult;
            if (!this.ValidateSignin(signin, out errorResult, out signInMessage))
                return errorResult;

            return await RenderResetPasswordPage(signInMessage, signin);
        }

        [Route(Constants.RoutePaths.ResetPassword)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IHttpActionResult> Reset(string signin, Login model)
        {
            Logger.Info("Reset password page submitted");

            if (this.options.AuthenticationOptions.EnableLocalLogin == false)
            {
                Logger.Warn("EnableLocalLogin disabled -- returning 405 MethodNotAllowed");
                return StatusCode(HttpStatusCode.MethodNotAllowed);
            }

            SignInMessage signInMessage;
            IHttpActionResult errorResult;
            if (!this.ValidateSignin(signin, out errorResult, out signInMessage))
                return errorResult;

            if (!(await IsLocalLoginAllowedForClient(signInMessage)))
            {
                Logger.ErrorFormat("Login not allowed for client {0}", signInMessage.ClientId);
                return RenderErrorPage();
            }

            if (model == null)
            {
                Logger.Error("No data submitted");
                return await RenderResetPasswordPage(signInMessage, signin, localizationService.GetMessage(MessageIds.InvalidUsernameOrPassword));
            }

            if (String.IsNullOrWhiteSpace(model.Username))
            {
                ModelState.AddModelError("Username", localizationService.GetMessage(MessageIds.UsernameRequired));
            }

            if (!ModelState.IsValid)
            {
                Logger.Warn("Validation error: Username missing");
                return await RenderResetPasswordPage(signInMessage, signin, ModelState.GetError(), model.Username);
            }

            if (model.Username.Length > options.InputLengthRestrictions.UserName)
            {
                Logger.Error("Username submitted beyond allowed length");
                return await RenderResetPasswordPage(signInMessage, signin);
            }

            var resetContext = new ResetPasswordContext { SignInMessage = signInMessage, UserName = model.Username };
            await passwordResetService.ResetPasswordAsync(resetContext);

            var resetResult = resetContext.ResetPasswordResult;
            if (resetResult == null)
            {
                Logger.WarnFormat("User service indicated incorrect username: {0}", model.Username);

                var errorMessage = localizationService.GetMessage(MessageIds.InvalidUsernameOrPassword);
                await eventService.RaiseResetPasswordFailureEventAsync(model.Username, signin, signInMessage, errorMessage);

                return await RenderResetPasswordPage(signInMessage, signin, errorMessage, model.Username);
            }

            if (resetResult.IsError)
            {
                Logger.WarnFormat("User service returned an error message: {0}", resetResult.ErrorMessage);

                await eventService.RaiseResetPasswordFailureEventAsync(model.Username, signin, signInMessage, resetResult.ErrorMessage);

                return await RenderResetPasswordPage(signInMessage, signin, resetResult.ErrorMessage, model.Username);
            }

            if (model.Username != resetContext.UserName)
            {
                Logger.Info($"Fixing model.Username from '{model.Username}' to '{resetContext.UserName}'");

                model.Username = resetContext.UserName;
            }

            this.lastUserNameCookie.SetValue(model.Username);

            Logger.InfoFormat("Reset password finished successfully for user '{0}'", model.Username);

            await eventService.RaiseResetPasswordSuccessEventAsync(model.Username, signin, signInMessage);

            var url = Request.GetOwinContext().GetIdentityServerBaseUrl().EnsureTrailingSlash() +
                Constants.RoutePaths.ResetPasswordVerify + "?signin=" + signin + "&username=" + model.Username;

            return Redirect(url);
        }

        [Route(Constants.RoutePaths.Login, Name = Constants.RouteNames.Login)]
        [HttpGet]
        public async Task<IHttpActionResult> Login(string signin = null)
        {
            Logger.Info("Login page requested");

            SignInMessage signInMessage;
            IHttpActionResult errorResult;
            if (!this.ValidateSignin(signin, out errorResult, out signInMessage))
                return errorResult;

            var preAuthContext = new PreAuthenticationContext { SignInMessage = signInMessage };
            await userService.PreAuthenticateAsync(preAuthContext);

            var authResult = preAuthContext.AuthenticateResult;
            if (authResult != null)
            {
                if (authResult.IsError)
                {
                    Logger.WarnFormat("User service returned an error message: {0}", authResult.ErrorMessage);

                    await eventService.RaisePreLoginFailureEventAsync(signin, signInMessage, authResult.ErrorMessage);

                    if (preAuthContext.ShowLoginPageOnErrorResult)
                    {
                        Logger.Debug("ShowLoginPageOnErrorResult set to true, showing login page with error");
                        return await RenderLoginPage(signInMessage, signin, authResult.ErrorMessage);
                    }
                    else
                    {
                        Logger.Debug("ShowLoginPageOnErrorResult set to false, showing error page with error");
                        return RenderErrorPage(authResult.ErrorMessage);
                    }
                }

                Logger.Info("User service returned a login result");

                await eventService.RaisePreLoginSuccessEventAsync(signin, signInMessage, authResult);

                return await SignInAndRedirectAsync(signInMessage, signin, authResult);
            }

            if (signInMessage.IdP.IsPresent())
            {
                Logger.InfoFormat("identity provider requested, redirecting to: {0}", signInMessage.IdP);
                return await LoginExternal(signin, signInMessage.IdP);
            }

            return await RenderLoginPage(signInMessage, signin);
        }

        [Route(Constants.RoutePaths.Login)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IHttpActionResult> LoginLocal(string signin, LoginCredentials model)
        {
            Logger.Info("Login page submitted");

            if (this.options.AuthenticationOptions.EnableLocalLogin == false)
            {
                Logger.Warn("EnableLocalLogin disabled -- returning 405 MethodNotAllowed");
                return StatusCode(HttpStatusCode.MethodNotAllowed);
            }

            SignInMessage signInMessage;
            IHttpActionResult errorResult;
            if (!this.ValidateSignin(signin, out errorResult, out signInMessage))
                return errorResult;

            if (!(await IsLocalLoginAllowedForClient(signInMessage)))
            {
                Logger.ErrorFormat("Login not allowed for client {0}", signInMessage.ClientId);
                return RenderErrorPage();
            }

            if (model == null)
            {
                Logger.Error("No data submitted");
                return await RenderLoginPage(signInMessage, signin, localizationService.GetMessage(MessageIds.InvalidUsernameOrPassword));
            }

            if (String.IsNullOrWhiteSpace(model.Username))
            {
                ModelState.AddModelError("Username", localizationService.GetMessage(MessageIds.UsernameRequired));
            }
            
            if (String.IsNullOrWhiteSpace(model.Password))
            {
                if (!string.IsNullOrWhiteSpace(model.ExternalProvider))
                {
                    this.lastUserNameCookie.SetValue(model.Username);

                    return await LoginExternal(signin, model.ExternalProvider, model.Username);
                }

                ModelState.AddModelError("Password", localizationService.GetMessage(MessageIds.PasswordRequired));
            }

            model.RememberMe = options.AuthenticationOptions.CookieOptions.CalculateRememberMeFromUserInput(model.RememberMe);

            if (!ModelState.IsValid)
            {
                Logger.Warn("validation error: username or password missing");
                return await RenderLoginPage(signInMessage, signin, ModelState.GetError(), model.Username, model.RememberMe == true);
            }

            if (model.Username.Length > options.InputLengthRestrictions.UserName || model.Password.Length > options.InputLengthRestrictions.Password)
            {
                Logger.Error("username or password submitted beyond allowed length");
                return await RenderLoginPage(signInMessage, signin);
            }

            var authenticationContext = new LocalAuthenticationContext
            {
                UserName = model.Username.Trim(),
                Password = model.Password.Trim(),
                SignInMessage = signInMessage
            };

            await userService.AuthenticateLocalAsync(authenticationContext);
            
            var authResult = authenticationContext.AuthenticateResult;
            if (authResult == null)
            {
                Logger.WarnFormat("user service indicated incorrect username or password for username: {0}", model.Username);
                
                var errorMessage = localizationService.GetMessage(MessageIds.InvalidUsernameOrPassword);
                await eventService.RaiseLocalLoginFailureEventAsync(model.Username, signin, signInMessage, errorMessage);
                
                return await RenderLoginPage(signInMessage, signin, errorMessage, model.Username, model.RememberMe == true);
            }

            if (authResult.IsForcePasswordChanged)
            {
                Logger.WarnFormat("User '{0}' is forced to change password", model.Username);

                await passwordResetService.HandlePasswordChangeForcedAsync(authenticationContext);

                authResult = authenticationContext.AuthenticateResult;

                if (authResult == null)
                {
                    Logger.Error("Password reset service returned no result");
                    return RenderErrorPage();
                }
            }

            if (authResult.IsError)
            {
                Logger.WarnFormat("user service returned an error message: {0}", authResult.ErrorMessage);

                await eventService.RaiseLocalLoginFailureEventAsync(model.Username, signin, signInMessage, authResult.ErrorMessage);
                
                return await RenderLoginPage(signInMessage, signin, authResult.ErrorMessage, model.Username, model.RememberMe == true);
            }

            Logger.Info("Login credentials successfully validated by user service");

            await eventService.RaiseLocalLoginSuccessEventAsync(model.Username, signin, signInMessage, authResult);

            this.lastUserNameCookie.SetValue(model.Username);

            return await SignInAndRedirectAsync(signInMessage, signin, authResult, model.RememberMe);
        }

        [Route(Constants.RoutePaths.LoginExternal, Name = Constants.RouteNames.LoginExternal)]
        [HttpGet]
        public async Task<IHttpActionResult> LoginExternal(string signin, string provider, string loginHint = null)
        {
            Logger.InfoFormat("External login requested for provider: {0}", provider);

            if (provider.IsMissing())
            {
                Logger.Error("No provider passed");
                return RenderErrorPage(localizationService.GetMessage(MessageIds.NoExternalProvider));
            }

            if (provider.Length > options.InputLengthRestrictions.IdentityProvider)
            {
                Logger.Error("Provider parameter passed was larger than max length");
                return RenderErrorPage();
            }

            SignInMessage signInMessage;
            IHttpActionResult errorResult;
            if (!this.ValidateSignin(signin, out errorResult, out signInMessage))
                return errorResult;

            if (!(await clientStore.IsValidIdentityProviderAsync(signInMessage.ClientId, provider)))
            {
                var msg = String.Format("External login error: provider {0} not allowed for client: {1}", provider, signInMessage.ClientId);
                Logger.ErrorFormat(msg);
                await eventService.RaiseFailureEndpointEventAsync(EventConstants.EndpointNames.Authenticate, msg);
                return RenderErrorPage();
            }
            
            if (context.IsValidExternalAuthenticationProvider(provider) == false)
            {
                var msg = String.Format("External login error: provider requested {0} is not a configured external provider", provider);
                Logger.ErrorFormat(msg);
                await eventService.RaiseFailureEndpointEventAsync(EventConstants.EndpointNames.Authenticate, msg);
                return RenderErrorPage();
            }

            var authProp = new Microsoft.Owin.Security.AuthenticationProperties
            {
                RedirectUri = Url.Route(Constants.RouteNames.LoginExternalCallback, null)
            };

            Logger.Info("Triggering challenge for external identity provider");

            // add the id to the dictionary so we can recall the cookie id on the callback
            authProp.Dictionary.Add(Constants.Authentication.SigninId, signin);
            authProp.Dictionary.Add(Constants.Authentication.KatanaAuthenticationType, provider);

            if (!string.IsNullOrEmpty(loginHint))
            {
                authProp.Dictionary.Add(Constants.Authentication.LoginHint, loginHint);
            }

            context.Authentication.Challenge(authProp, provider);
            
            return Unauthorized();
        }

        [Route(Constants.RoutePaths.LoginExternalCallback, Name = Constants.RouteNames.LoginExternalCallback)]
        [HttpGet]
        public async Task<IHttpActionResult> LoginExternalCallback(string error = null)
        {
            Logger.Info("Callback invoked from external identity provider");
            
            if (error.IsPresent())
            {
                if (error.Length > options.InputLengthRestrictions.ExternalError) error = error.Substring(0, options.InputLengthRestrictions.ExternalError);

                Logger.ErrorFormat("External identity provider returned error: {0}", error);
                await eventService.RaiseExternalLoginErrorEventAsync(error);
                return RenderErrorPage(String.Format(localizationService.GetMessage(MessageIds.ExternalProviderError), error));
            }

            var signInId = await context.GetSignInIdFromExternalProvider();
            if (signInId.IsMissing())
            {
                Logger.Info("No signin id passed");
                return HandleNoSignin();
            }

            var signInMessage = signInMessageCookie.Read(signInId);
            if (signInMessage == null)
            {
                Logger.Info("No cookie matching signin id found");
                return HandleNoSignin();
            }

            var user = await context.GetIdentityFromExternalProvider();
            if (user == null)
            {
                Logger.Error("No identity from external identity provider");
                return await RenderLoginPage(signInMessage, signInId, localizationService.GetMessage(MessageIds.NoMatchingExternalAccount));
            }

            var externalIdentity = ExternalIdentity.FromClaims(user.Claims);
            if (externalIdentity == null)
            {
                var claims = user.Claims.Select(x => new { x.Type, x.Value });
                Logger.ErrorFormat("No subject or unique identifier claims from external identity provider. Claims provided:\r\n{0}", LogSerializer.Serialize(claims));
                return await RenderLoginPage(signInMessage, signInId, localizationService.GetMessage(MessageIds.NoMatchingExternalAccount));
            }

            Logger.InfoFormat("External user provider: {0}, provider ID: {1}", externalIdentity.Provider, externalIdentity.ProviderId);

            var externalContext = new ExternalAuthenticationContext
            {
                ExternalIdentity = externalIdentity,
                SignInMessage = signInMessage
            };

            await userService.AuthenticateExternalAsync(externalContext);
            
            var authResult = externalContext.AuthenticateResult;
            if (authResult == null)
            {
                Logger.Warn("User service failed to authenticate external identity");
                
                var msg = localizationService.GetMessage(MessageIds.NoMatchingExternalAccount);
                await eventService.RaiseExternalLoginFailureEventAsync(externalIdentity, signInId, signInMessage, msg);
                
                return await RenderLoginPage(signInMessage, signInId, msg);
            }

            if (authResult.IsError)
            {
                Logger.WarnFormat("User service returned error message: {0}", authResult.ErrorMessage);

                await eventService.RaiseExternalLoginFailureEventAsync(externalIdentity, signInId, signInMessage, authResult.ErrorMessage);
                
                return await RenderLoginPage(signInMessage, signInId, authResult.ErrorMessage);
            }

            Logger.Info("External identity successfully validated by user service");

            await eventService.RaiseExternalLoginSuccessEventAsync(externalIdentity, signInId, signInMessage, authResult);

            return await SignInAndRedirectAsync(signInMessage, signInId, authResult);
        }

        [Route(Constants.RoutePaths.ResumeLoginFromRedirect, Name = Constants.RouteNames.ResumeLoginFromRedirect)]
        [HttpGet]
        public async Task<IHttpActionResult> ResumeLoginFromRedirect(string resume)
        {
            Logger.Info("Callback requested to resume login from partial login");

            if (resume.IsMissing())
            {
                Logger.Error("no resumeId passed");
                return RenderErrorPage();
            }

            if (resume.Length > MaxSignInMessageLength)
            {
                Logger.Error("resumeId length longer than allowed length");
                return RenderErrorPage();
            }

            var user = await context.GetIdentityFromPartialSignIn();
            if (user == null)
            {
                Logger.Error("no identity from partial login");
                return RenderErrorPage();
            }

            var type = GetClaimTypeForResumeId(resume);
            var resumeClaim = user.FindFirst(type);
            if (resumeClaim == null)
            {
                Logger.Error("no claim matching resumeId");
                return RenderErrorPage();
            }

            var signInId = resumeClaim.Value;
            if (signInId.IsMissing())
            {
                Logger.Error("No signin id found in resume claim");
                return RenderErrorPage();
            }

            var signInMessage = signInMessageCookie.Read(signInId);
            if (signInMessage == null)
            {
                Logger.Error("No cookie matching signin id found");
                return RenderErrorPage();
            }

            AuthenticateResult result = null;

            // determine which return path the user is taking -- are they coming from
            // a ExternalProvider partial logon, or not
            var externalProviderClaim = user.FindFirst(Constants.ClaimTypes.ExternalProviderUserId);

            // cleanup the claims from the partial login
            if (user.HasClaim(c => c.Type == Constants.ClaimTypes.PartialLoginRestartUrl))
            {
                user.RemoveClaim(user.FindFirst(Constants.ClaimTypes.PartialLoginRestartUrl));
            }
            if (user.HasClaim(c => c.Type == Constants.ClaimTypes.PartialLoginReturnUrl))
            {
                user.RemoveClaim(user.FindFirst(Constants.ClaimTypes.PartialLoginReturnUrl));
            }
            if (user.HasClaim(c => c.Type == Constants.ClaimTypes.ExternalProviderUserId))
            {
                user.RemoveClaim(user.FindFirst(Constants.ClaimTypes.ExternalProviderUserId));
            }
            if (user.HasClaim(c => c.Type == GetClaimTypeForResumeId(resume)))
            {
                user.RemoveClaim(user.FindFirst(GetClaimTypeForResumeId(resume)));
            }

            if (externalProviderClaim != null)
            {
                Logger.Info("using ExternalProviderUserId to call AuthenticateExternalAsync");

                var provider = externalProviderClaim.Issuer;
                var providerId = externalProviderClaim.Value;
                var externalIdentity = new ExternalIdentity
                {
                    Provider = provider,
                    ProviderId = providerId,
                    Claims = user.Claims
                };

                Logger.InfoFormat("external user provider: {0}, provider ID: {1}", externalIdentity.Provider, externalIdentity.ProviderId);

                var externalContext = new ExternalAuthenticationContext
                {
                    ExternalIdentity = externalIdentity,
                    SignInMessage = signInMessage
                };

                await userService.AuthenticateExternalAsync(externalContext);

                result = externalContext.AuthenticateResult;
                if (result == null)
                {
                    Logger.Warn("user service failed to authenticate external identity");

                    var msg = localizationService.GetMessage(MessageIds.NoMatchingExternalAccount);
                    await eventService.RaiseExternalLoginFailureEventAsync(externalIdentity, signInId, signInMessage, msg);

                    return await RenderLoginPage(signInMessage, signInId, msg);
                }

                if (result.IsError)
                {
                    Logger.WarnFormat("user service returned error message: {0}", result.ErrorMessage);

                    await eventService.RaiseExternalLoginFailureEventAsync(externalIdentity, signInId, signInMessage, result.ErrorMessage);

                    return await RenderLoginPage(signInMessage, signInId, result.ErrorMessage);
                }

                Logger.Info("External identity successfully validated by user service");

                await eventService.RaiseExternalLoginSuccessEventAsync(externalIdentity, signInId, signInMessage, result);
            }
            else
            {
                // check to see if the resultant user has all the claim types needed to login
                if (!Constants.AuthenticateResultClaimTypes.All(claimType => user.HasClaim(c => c.Type == claimType)))
                {
                    Logger.Error("Missing AuthenticateResultClaimTypes -- rendering error page");
                    return RenderErrorPage();
                }

                // this is a normal partial login continuation
                Logger.Info("Partial login resume success -- logging user in");

                result = new AuthenticateResult(new ClaimsPrincipal(user));

                await eventService.RaisePartialLoginCompleteEventAsync(result.User.Identities.First(), signInId, signInMessage);
            }

            // check to see if user clicked "remember me" on login page
            bool? rememberMe = await context.GetPartialLoginRememberMeAsync();

            return await SignInAndRedirectAsync(signInMessage, signInId, result, rememberMe);
        }

        [Route(Constants.RoutePaths.Logout, Name = Constants.RouteNames.LogoutPrompt)]
        [HttpGet]
        public async Task<IHttpActionResult> LogoutPrompt(string id = null)
        {
            if (id != null && id.Length > MaxSignInMessageLength)
            {
                Logger.Error("Logout prompt requested, but id param is longer than allowed length");
                return RenderErrorPage();
            }

            var user = (ClaimsPrincipal)User;
            if (user == null || user.Identity.IsAuthenticated == false)
            {
                // user is already logged out, so just trigger logout cleanup
                return await Logout(id);
            }

            var sub = user.GetSubjectId();
            Logger.InfoFormat("Logout prompt for subject: {0}", sub);

            if (options.AuthenticationOptions.RequireSignOutPrompt == false)
            {
                var message = signOutMessageCookie.Read(id);
                if (message != null && message.ClientId.IsPresent())
                {
                    var client = await clientStore.FindClientByIdAsync(message.ClientId);
                    if (client != null && client.RequireSignOutPrompt == true)
                    {
                        Logger.InfoFormat("SignOutMessage present (from client {0}) but RequireSignOutPrompt is true, rendering logout prompt", message.ClientId);
                        return RenderLogoutPromptPage(id);
                    }

                    Logger.InfoFormat("SignOutMessage present (from client {0}) and RequireSignOutPrompt is false, performing logout", message.ClientId);
                    return await Logout(id);
                }

                if (!this.options.AuthenticationOptions.EnableSignOutPrompt)
                {
                    Logger.InfoFormat("EnableSignOutPrompt set to false, performing logout");
                    return await Logout(id);
                }

                Logger.InfoFormat("EnableSignOutPrompt set to true, rendering logout prompt");
            }
            else
            {
                Logger.InfoFormat("RequireSignOutPrompt set to true, rendering logout prompt");
            }

            return RenderLogoutPromptPage(id);
        }

        [Route(Constants.RoutePaths.Logout, Name = Constants.RouteNames.Logout)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IHttpActionResult> Logout(string id = null)
        {
            Logger.Info("Logout endpoint submitted");

            if (id != null && id.Length > MaxSignInMessageLength)
            {
                Logger.Error("id param is longer than allowed length");
                return RenderErrorPage();
            }
            
            var user = (ClaimsPrincipal)User;
            if (user != null && user.Identity.IsAuthenticated)
            {
                var sub = user.GetSubjectId();
                Logger.InfoFormat("Logout requested for subject: {0}", sub);
            }

            Logger.Info("Clearing cookies");
            context.QueueRemovalOfSignOutMessageCookie(id);
            context.ClearAuthenticationCookies();
            context.SignOutOfExternalIdP(id);

            Logger.Info("Clearing UserName cookie");
            this.lastUserNameCookie.SetValue(null);

            string clientId = null;
            var message = signOutMessageCookie.Read(id);
            if (message != null)
            {
                clientId = message.ClientId;
            }
            await context.CallUserServiceSignOutAsync(clientId);

            if (user != null && user.Identity.IsAuthenticated)
            {
                await eventService.RaiseLogoutEventAsync(user, id, message);
            }

            return await RenderLoggedOutPage(id);
        }

        private IHttpActionResult HandleNoSignin()
        {
            if (options.AuthenticationOptions.InvalidSignInRedirectUrl.IsMissing())
            {
                return RenderErrorPage(localizationService.GetMessage(MessageIds.NoSignInCookie));
            }

            var url = options.AuthenticationOptions.InvalidSignInRedirectUrl;
            if (url.StartsWith("~/"))
            {
                url = url.Substring(2);
                url = Request.GetOwinEnvironment().GetIdentityServerBaseUrl() + url;
            }
            else if (url.StartsWith("/"))
            {
                url = Request.GetOwinEnvironment().GetIdentityServerHost() + url;
            }
            else
            {
                url = options.AuthenticationOptions.InvalidSignInRedirectUrl;
            }

            return Redirect(url);
        }
        
        private async Task<IHttpActionResult> SignInAndRedirectAsync(SignInMessage signInMessage, string signInMessageId, AuthenticateResult authResult, bool? rememberMe = null)
        {
            var postAuthenActionResult = await PostAuthenticateAsync(signInMessage, signInMessageId, authResult);
            if (postAuthenActionResult != null)
            {
                if (postAuthenActionResult.Item1 != null)
                {
                    return postAuthenActionResult.Item1;
                }

                if (postAuthenActionResult.Item2 != null)
                {
                    authResult = postAuthenActionResult.Item2;
                }
            }

            // check to see if idp used to signin matches 
            if (signInMessage.IdP.IsPresent() && 
                authResult.IsPartialSignIn == false && 
                authResult.HasSubject && 
                authResult.User.GetIdentityProvider() != signInMessage.IdP)
            {
                // this is an error -- the user service did not set the idp to the one requested
                Logger.ErrorFormat("IdP requested was: {0}, but the user service issued signin for IdP: {1}", signInMessage.IdP, authResult.User.GetIdentityProvider());
                return RenderErrorPage();
            }

            ClearAuthenticationCookiesForNewSignIn(authResult);
            IssueAuthenticationCookie(signInMessageId, authResult, rememberMe);

            var redirectUrl = GetRedirectUrl(signInMessage, authResult);
            Logger.InfoFormat("redirecting to: {0}", redirectUrl);
            return Redirect(redirectUrl);
        }

        private async Task<Tuple<IHttpActionResult, AuthenticateResult>> PostAuthenticateAsync(SignInMessage signInMessage, string signInMessageId, AuthenticateResult result)
        {
            if (result.IsPartialSignIn == false)
            {
                Logger.Info("Calling PostAuthenticateAsync on the user service");

                var ctx = new PostAuthenticationContext
                {
                    SignInMessage = signInMessage,
                    AuthenticateResult = result
                };
                await userService.PostAuthenticateAsync(ctx);

                var authResult = ctx.AuthenticateResult;
                if (authResult == null)
                {
                    Logger.Error("user service PostAuthenticateAsync returned a null AuthenticateResult");
                    return new Tuple<IHttpActionResult,AuthenticateResult>(RenderErrorPage(), null);
                }

                if (authResult.IsError)
                {
                    Logger.WarnFormat("user service PostAuthenticateAsync returned an error message: {0}", authResult.ErrorMessage);
                    if (ctx.ShowLoginPageOnErrorResult)
                    {
                        Logger.Debug("ShowLoginPageOnErrorResult set to true, showing login page with error");
                        return new Tuple<IHttpActionResult, AuthenticateResult>(await RenderLoginPage(signInMessage, signInMessageId, authResult.ErrorMessage), null);
                    }
                    else
                    {
                        Logger.Debug("ShowLoginPageOnErrorResult set to false, showing error page with error");
                        return new Tuple<IHttpActionResult, AuthenticateResult>(RenderErrorPage(authResult.ErrorMessage), null);
                    }
                }

                if (result != authResult)
                {
                    result = authResult;
                    Logger.Info("user service PostAuthenticateAsync returned a different AuthenticateResult");
                }
            }
            
            return new Tuple<IHttpActionResult, AuthenticateResult>(null, result);
        }


        private void IssueAuthenticationCookie(string signInMessageId, AuthenticateResult authResult, bool? rememberMe = null)
        {
            if (authResult == null) throw new ArgumentNullException("authResult");

            if (authResult.IsPartialSignIn)
            {
                Logger.Info("issuing partial signin cookie");
            }
            else
            {
                Logger.Info("issuing primary signin cookie");
            }

            var props = new Microsoft.Owin.Security.AuthenticationProperties();

            var id = authResult.User.Identities.First();
            if (authResult.IsPartialSignIn)
            {
                // add claim so partial redirect can return here to continue login
                // we need a random ID to resume, and this will be the query string
                // to match a claim added. the claim added will be the original 
                // signIn ID. 
                var resumeId = CryptoRandom.CreateUniqueId();

                var resumeLoginUrl = context.GetPartialLoginResumeUrl(resumeId);
                var resumeLoginClaim = new Claim(Constants.ClaimTypes.PartialLoginReturnUrl, resumeLoginUrl);
                id.AddClaim(resumeLoginClaim);
                id.AddClaim(new Claim(GetClaimTypeForResumeId(resumeId), signInMessageId));

                // add url to start login process over again (which re-triggers preauthenticate)
                var restartUrl = context.GetPartialLoginRestartUrl(signInMessageId);
                id.AddClaim(new Claim(Constants.ClaimTypes.PartialLoginRestartUrl, restartUrl));
            }
            else
            {
                signInMessageCookie.Clear(signInMessageId);
                sessionCookie.IssueSessionId(rememberMe);
            }

            if (!authResult.IsPartialSignIn)
            {
                // don't issue persistnt cookie if it's a partial signin
                if (rememberMe == true ||
                    (rememberMe != false && this.options.AuthenticationOptions.CookieOptions.IsPersistent))
                {
                    // only issue persistent cookie if user consents (rememberMe == true) or
                    // if server is configured to issue persistent cookies and user has not explicitly
                    // denied the rememberMe (false)
                    // if rememberMe is null, then user was not prompted for rememberMe
                    props.IsPersistent = true;
                    if (rememberMe == true)
                    {
                        var expires = DateTimeHelper.UtcNow.Add(options.AuthenticationOptions.CookieOptions.RememberMeDuration);
                        props.ExpiresUtc = new DateTimeOffset(expires);
                    }
                }
            }
            else
            {
                if (rememberMe != null)
                {
                    // if rememberme set, then store for later use once we need to issue login cookie
                    props.Dictionary.Add(Constants.Authentication.PartialLoginRememberMe, rememberMe.Value ? "true" : "false");
                }
            }

            context.Authentication.SignIn(props, id);
        }

        private static string GetClaimTypeForResumeId(string resume)
        {
            return String.Format(Constants.ClaimTypes.PartialLoginResumeId, resume);
        }

        private Uri GetRedirectUrl(SignInMessage signInMessage, AuthenticateResult authResult)
        {
            if (signInMessage == null) throw new ArgumentNullException("signInMessage");
            if (authResult == null) throw new ArgumentNullException("authResult");

            if (authResult.IsPartialSignIn)
            {
                var path = authResult.PartialSignInRedirectPath;
                if (path.StartsWith("~/"))
                {
                    path = path.Substring(2);
                    path = this.context.GetIdentityServerBaseUrl() + path;
                }
                var host = new Uri(this.context.GetIdentityServerHost());
                return new Uri(host, path);
            }
            else
            {
                return new Uri(signInMessage.ReturnUrl);
            }
        }

        private void ClearAuthenticationCookiesForNewSignIn(AuthenticateResult authResult)
        {
            // on a partial sign-in, preserve the existing primary sign-in
            if (!authResult.IsPartialSignIn)
            {
                context.Authentication.SignOut(Constants.PrimaryAuthenticationType);
            }
            context.Authentication.SignOut(
                Constants.ExternalAuthenticationType,
                Constants.PartialSignInAuthenticationType);
        }

        async Task<bool> IsLocalLoginAllowedForClient(SignInMessage message)
        {
            if (message != null && message.ClientId.IsPresent())
            {
                var client = await clientStore.FindClientByIdAsync(message.ClientId);
                if (client != null)
                {
                    return client.EnableLocalLogin;
                }
            }

            return true;
        }

        private async Task<IHttpActionResult> RenderResetPasswordCallbackPage(string token, string errorMessage = null, string username = null, string signInMessageId = null)
        {
            var isLocalLoginAllowed = options.AuthenticationOptions.EnableLocalLogin;

            if (errorMessage != null)
            {
                Logger.InfoFormat("Rendering reset password callback page with error message: {0}", errorMessage);
            }
            else
            {
                if (isLocalLoginAllowed == false)
                {
                    if (options.AuthenticationOptions.EnableLocalLogin)
                    {
                        Logger.Info("Local login disabled");
                    }
                }

                Logger.Info("Rendering reset password callback page");
            }

            var resetPasswordModel = new ResetPasswordViewModel
            {
                RequestId = context.GetRequestId(),
                SiteName = options.SiteName,
                SiteUrl = Request.GetIdentityServerBaseUrl(),
                CurrentUrl = context.Request.Uri.AbsoluteUri,
                ErrorMessage = errorMessage,
                ResetPasswordUrl = isLocalLoginAllowed ? Url.Route(Constants.RouteNames.ResetPasswordCallback, new { token, signin = signInMessageId }) : null,
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = context.GetIdentityServerLogoutUrl(),
                AntiForgery = antiForgeryToken.GetAntiForgeryToken(),
                Username = username,
                IsFromSignIn = !string.IsNullOrEmpty(signInMessageId)
            };

            return new ResetPasswordCallbackActionResult(viewService, resetPasswordModel, token);
        }

        private async Task<IHttpActionResult> RenderResetPasswordVerifyPage(SignInMessage message, string signInMessageId, string errorMessage = null, string username = null)
        {
            if (message == null) throw new ArgumentNullException("message");

            username = GetUserNameForLoginPage(message, username, out _);

            var isLocalLoginAllowedForClient = await IsLocalLoginAllowedForClient(message);
            var isLocalLoginAllowed = isLocalLoginAllowedForClient && options.AuthenticationOptions.EnableLocalLogin;
            var client = await clientStore.FindClientByIdAsync(message.ClientId);

            if (errorMessage != null)
            {
                Logger.InfoFormat("Rendering reset password verify page with error message: {0}", errorMessage);
            }
            else
            {
                if (isLocalLoginAllowed == false)
                {
                    if (options.AuthenticationOptions.EnableLocalLogin)
                    {
                        Logger.Info("Local login disabled");
                    }
                    if (isLocalLoginAllowedForClient)
                    {
                        Logger.Info("Local login disabled for the client");
                    }
                }

                Logger.Info("Rendering reset password verify page");
            }

            var resetPasswordModel = new ResetPasswordViewModel
            {
                RequestId = context.GetRequestId(),
                SiteName = options.SiteName,
                SiteUrl = Request.GetIdentityServerBaseUrl(),
                CurrentUrl = context.Request.Uri.AbsoluteUri,
                ErrorMessage = errorMessage,
                ResetPasswordUrl = isLocalLoginAllowed ? Url.Route(Constants.RouteNames.ResetPasswordVerify, new { signin = signInMessageId }) : null,
                LoginUrl = isLocalLoginAllowed ? Url.Route(Constants.RouteNames.Login, new { signin = signInMessageId }) : null,
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = context.GetIdentityServerLogoutUrl(),
                AntiForgery = antiForgeryToken.GetAntiForgeryToken(),
                Username = username,
                ClientName = client != null ? client.ClientName : null,
                ClientUrl = client != null ? client.ClientUri : null,
                ClientLogoUrl = client != null ? client.LogoUri : null,
                IsFromSignIn = !string.IsNullOrEmpty(signInMessageId)
            };

            return new ResetPasswordVerifyActionResult(viewService, resetPasswordModel, message);
        }

        private async Task<IHttpActionResult> RenderResetPasswordPage(SignInMessage message, string signInMessageId, string errorMessage = null, string username = null)
        {
            if (message == null) throw new ArgumentNullException("message");

            username = GetUserNameForLoginPage(message, username, out _);

            var isLocalLoginAllowedForClient = await IsLocalLoginAllowedForClient(message);
            var isLocalLoginAllowed = isLocalLoginAllowedForClient && options.AuthenticationOptions.EnableLocalLogin;
            var client = await clientStore.FindClientByIdAsync(message.ClientId);

            if (errorMessage != null)
            {
                Logger.InfoFormat("Rendering reset password page with error message: {0}", errorMessage);
            }
            else
            {
                if (isLocalLoginAllowed == false)
                {
                    if (options.AuthenticationOptions.EnableLocalLogin)
                    {
                        Logger.Info("Local login disabled");
                    }
                    if (isLocalLoginAllowedForClient)
                    {
                        Logger.Info("Local login disabled for the client");
                    }
                }

                Logger.Info("Rendering reset password page");
            }

            var resetPasswordModel = new ResetPasswordViewModel
            {
                RequestId = context.GetRequestId(),
                SiteName = options.SiteName,
                SiteUrl = Request.GetIdentityServerBaseUrl(),
                CurrentUrl = context.Request.Uri.AbsoluteUri,
                ErrorMessage = errorMessage,
                ResetPasswordUrl = isLocalLoginAllowed ? Url.Route(Constants.RouteNames.ResetPassword, new { signin = signInMessageId }) : null,
                LoginUrl = isLocalLoginAllowed ? Url.Route(Constants.RouteNames.Login, new { signin = signInMessageId }) : null,
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = context.GetIdentityServerLogoutUrl(),
                AntiForgery = antiForgeryToken.GetAntiForgeryToken(),
                Username = username,
                ClientName = client != null ? client.ClientName : null,
                ClientUrl = client != null ? client.ClientUri : null,
                ClientLogoUrl = client != null ? client.LogoUri : null,
                IsFromSignIn = !string.IsNullOrEmpty(signInMessageId)
            };

            return new ResetPasswordActionResult(viewService, resetPasswordModel, message);
        }

        private async Task<IHttpActionResult> RenderLoginPage(SignInMessage message, string signInMessageId, string errorMessage = null, string username = null, bool rememberMe = false)
        {
            if (message == null) throw new ArgumentNullException("message");

            username = GetUserNameForLoginPage(message, username, out bool isUserNameFromCookie);

            var isLocalLoginAllowedForClient = await IsLocalLoginAllowedForClient(message);
            var isLocalLoginAllowed = isLocalLoginAllowedForClient && options.AuthenticationOptions.EnableLocalLogin;

            var idpRestrictions = await clientStore.GetIdentityProviderRestrictionsAsync(message.ClientId);
            var providers = context.GetExternalAuthenticationProviders(idpRestrictions);
            var providerLinks = context.GetLinksFromProviders(providers, signInMessageId);
            var visibleLinks = providerLinks.FilterHiddenLinks();
            var client = await clientStore.FindClientByIdAsync(message.ClientId);

            if (errorMessage != null)
            {
                Logger.InfoFormat("rendering login page with error message: {0}", errorMessage);
            }
            else
            {
                if (isLocalLoginAllowed == false ||
                    (providerLinks.Any() && (message.LoginForced == LoginForced.Forced || message.LoginForced == LoginForced.ForcedHidden || isUserNameFromCookie)))
                {
                    if (options.AuthenticationOptions.EnableLocalLogin)
                    {
                        Logger.Info("local login disabled");
                    }
                    if (isLocalLoginAllowedForClient)
                    {
                        Logger.Info("local login disabled for the client");
                    }

                    string providerType = null;

                    if (!providerLinks.Any())
                    {
                        Logger.Info("no providers registered for client");
                        return RenderErrorPage();
                    }
                    else if (providerLinks.Count() == 1)
                    {
                        Logger.Info("only one provider for client");
                        providerType = providerLinks.First().Type;
                    }
                    else if (visibleLinks.Count() == 1)
                    {
                        Logger.Info("only one visible provider");
                        providerType = visibleLinks.First().Type;
                    }

                    if (providerType.IsPresent())
                    {
                        Logger.InfoFormat("redirecting to provider: {0}", providerType);

                        return await LoginExternal(signInMessageId, providerType, username);
                    }
                }

                Logger.Info("rendering login page");
            }

            var loginPageLinks = options.AuthenticationOptions.LoginPageLinks.Render(Request.GetIdentityServerBaseUrl(), signInMessageId);

            var loginModel = new LoginViewModel
            {
                RequestId = context.GetRequestId(),
                SiteName = options.SiteName,
                SiteUrl = Request.GetIdentityServerBaseUrl(),
                CurrentUrl = context.Request.Uri.AbsoluteUri,
                ExternalProviders = visibleLinks,
                AdditionalLinks = loginPageLinks,
                ErrorMessage = errorMessage,
                LoginUrl = isLocalLoginAllowed ? Url.Route(Constants.RouteNames.Login, new { signin = signInMessageId }) : null,
                AllowRememberMe = options.AuthenticationOptions.CookieOptions.AllowRememberMe,
                RememberMe = options.AuthenticationOptions.CookieOptions.AllowRememberMe && rememberMe,
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = context.GetIdentityServerLogoutUrl(),
                AntiForgery = antiForgeryToken.GetAntiForgeryToken(),
                Username = username,
                ClientName = client != null ? client.ClientName : null,
                ClientUrl = client != null ? client.ClientUri : null,
                ClientLogoUrl = client != null ? client.LogoUri : null
            };

            if (client != null && !client.AllowRememberMe)
            {
                loginModel.AllowRememberMe = false;
            }

            if (options.AuthenticationOptions.EnableLoginHint)
            {
                loginModel.UsernameReadonly = message.LoginForced == LoginForced.Forced;
                loginModel.UsernameHidden = message.LoginForced == LoginForced.ForcedHidden;
            }

            return new LoginActionResult(viewService, loginModel, message);
        }

        private string GetUserNameForLoginPage(SignInMessage message, string username, out bool isFromCookie)
        {
            isFromCookie = false;

            if (username.IsMissing() && message.LoginHint.IsPresent())
            {
                if (options.AuthenticationOptions.EnableLoginHint)
                {
                    Logger.InfoFormat("Using LoginHint for username: {0}", message.LoginHint);
                    username = message.LoginHint;
                }
                else
                {
                    Logger.Warn("Not using LoginHint because EnableLoginHint is false");
                }
            }

            var lastUsernameCookieValue = this.lastUserNameCookie.GetValue();
            if (username.IsMissing() && lastUsernameCookieValue.IsPresent())
            {
                Logger.InfoFormat("Using LastUserNameCookie value for username: {0}", lastUsernameCookieValue);
                username = lastUsernameCookieValue;
                isFromCookie = true;
            }

            return username;
        }

        private IHttpActionResult RenderLogoutPromptPage(string id)
        {
            var logout_url = context.GetIdentityServerLogoutUrl();
            if (id.IsPresent())
            {
                logout_url += "?id=" + id;
            }

            var logoutModel = new LogoutViewModel
            {
                SiteName = options.SiteName,
                SiteUrl = context.GetIdentityServerBaseUrl(),
                CurrentUrl = context.Request.Uri.AbsoluteUri,
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = logout_url,
                AntiForgery = antiForgeryToken.GetAntiForgeryToken(),
            };

            var message = signOutMessageCookie.Read(id);
            return new LogoutActionResult(viewService, logoutModel, message);
        }

        private async Task<IHttpActionResult> RenderLoggedOutPage(string id)
        {
            Logger.Info("rendering logged out page");

            var baseUrl = context.GetIdentityServerBaseUrl();
            var iframeUrls = options.RenderProtocolUrls(baseUrl, sessionCookie.GetSessionId());

            var message = signOutMessageCookie.Read(id);
            string redirectUrl = null;
            string clientName = null;

            if (message != null)
            {
                redirectUrl = message.ReturnUrl;
                if (redirectUrl != null && redirectUrl.StartsWith("~/"))
                {
                    redirectUrl = System.Web.VirtualPathUtility.ToAbsolute(redirectUrl);
                }

                clientName = await clientStore.GetClientName(message);

                if (!string.IsNullOrEmpty(message.UiLocales))
                {
                    this.context.Environment.SetRequestLanguage(message.UiLocales);
                }
            }

            var loggedOutModel = new LoggedOutViewModel
            {
                SiteName = options.SiteName,
                SiteUrl = baseUrl,
                CurrentUrl = context.Request.Uri.AbsoluteUri,
                IFrameUrls = iframeUrls,
                ClientName = clientName,
                RedirectUrl = redirectUrl,
                AutoRedirect = options.AuthenticationOptions.EnablePostSignOutAutoRedirect,
                AutoRedirectDelay = options.AuthenticationOptions.PostSignOutAutoRedirectDelay
            };
            return new LoggedOutActionResult(viewService, loggedOutModel, message);
        }

        private IHttpActionResult RenderErrorPage(string message = null)
        {
            message = message ?? localizationService.GetMessage(MessageIds.UnexpectedError);
            var errorModel = new ErrorViewModel
            {
                RequestId = context.GetRequestId(),
                SiteName = this.options.SiteName,
                SiteUrl = context.GetIdentityServerBaseUrl(),
                CurrentUrl = context.Request.Uri.AbsoluteUri,
                ErrorMessage = message,
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = context.GetIdentityServerLogoutUrl(),
            };
            var errorResult = new ErrorActionResult(viewService, errorModel);
            return errorResult;
        }

        private bool ValidateSignin(string signin, out IHttpActionResult errorResult, out SignInMessage signInMessage)
        {
            signInMessage = null;

            if (signin.IsMissing())
            {
                Logger.Info("No signin id passed");
                errorResult = HandleNoSignin();
                return false;
            }

            if (signin.Length > MaxSignInMessageLength)
            {
                Logger.Error("Signin parameter passed was larger than max length");
                errorResult = RenderErrorPage();
                return false;
            }

            signInMessage = signInMessageCookie.Read(signin);
            if (signInMessage == null)
            {
                Logger.Info("No cookie matching signin id found");
                errorResult = HandleNoSignin();
                return false;
            }

            Logger.DebugFormat("Signin message passed: {0}", JsonConvert.SerializeObject(signInMessage, Formatting.Indented));

            if (!string.IsNullOrEmpty(signInMessage.UiLocales))
            {
                this.context.Environment.SetRequestLanguage(signInMessage.UiLocales);
            }

            errorResult = null;
            return true;
        }
    }
}