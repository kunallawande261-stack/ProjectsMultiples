// eslint-disable-next-line
// @ts-nocheck
(function (global) {
	const defaultProtocolInfo = {
		authorization_header: 'Authorization',
		www_authenticate_header: 'WWW-Authenticate',
		unauthorized_status_code: 401
	};
	let protocolInfo;

	const oidcSettings = {
		authority: '',

		client_id: '',

		redirect_uri: '',
		popup_redirect_uri: '',
		silent_redirect_uri: '',

		post_logout_redirect_uri: '',

		response_type: 'code',

		scope: 'openid Innovator offline_access',

		accessTokenExpiringNotificationTimeInSeconds: 5 * 60, // 5 min
		automaticSilentRenew: true,
		revokeTokensOnSignout: true,

		loadUserInfo: false,
		monitorSession: false,

		popupWindowFeatures:
			'location=no,toolbar=no,width=1000,height=550,left=200,top=150;' // TODO: Implement resize on load in OAuthServer/login page
	};

	let oidc;
	let oidcUserManager;
	let oidcUser;

	global.OAuthClient = {
		init: function (oauthClientId, baseClientUrl, oauthServerConfiguration) {
			// eslint-disable-next-line @typescript-eslint/no-this-alias
			const self = this;
			oidc = null;
			oidcUserManager = null;
			oidcUser = null;
			const authorizeEndpoint = oauthServerConfiguration.authorization_endpoint;
			const authority = authorizeEndpoint.replace(/connect\/authorize$/i, '');
			protocolInfo = Object.assign(
				{},
				defaultProtocolInfo,
				oauthServerConfiguration.protocol_info
			);
			return new Promise(function (resolve, reject) {
				require(['Vendors/oidc-client-ts.min'], function () {
					try {
						oidc = window.oidc;
						oidcSettings.authority = authority;
						oidcSettings.metadata = oauthServerConfiguration; // To avoid extra request for metadata by openid-client library
						oidcSettings.client_id = oauthClientId;
						oidcSettings.redirect_uri =
							baseClientUrl + 'OAuth/RedirectCallback';
						oidcSettings.popup_redirect_uri =
							baseClientUrl + 'OAuth/PopupCallback';
						oidcSettings.silent_redirect_uri =
							baseClientUrl + 'OAuth/SilentCallback';
						oidcSettings.post_logout_redirect_uri =
							baseClientUrl + 'OAuth/PostLogoutCallback';
						const storage = new oidc.WebStorageStateStore({
							store: window.sessionStorage
						});
						oidcSettings.userStore = storage;
						oidcUserManager = new oidc.UserManager(oidcSettings);
						oidcUserManager.events.addUserLoaded(self._onUserLoaded);
						oidcUserManager.events.addUserUnloaded(self._onUserUnloaded);
						oidcUserManager.events.addUserSignedOut(self._onUserSignedOut);
						resolve();
					} catch (e) {
						reject(
							new Error(
								'An error occurred while trying to configure OidcClient library."'
							)
						);
					}
				});
			});
		},

		login: function (options) {
			options = this._convertOptionsToOidcSettings(options);
			let silentPromise;
			if (
				options.prompt === 'login' ||
				options.prompt === 'select_account' ||
				!this._isSilentRenewActive()
			) {
				silentPromise = Promise.reject(new Error('Require login page.'));
			} else {
				// User from sessionStorage will be returned only on page refreshing (F5).
				// In new tab/window it will be null.
				silentPromise = oidcUserManager.getUser();
			}
			return silentPromise
				.then(function (user) {
					// Check additionally that access token is not expired
					if (user === null || user.expired) {
						return Promise.reject(new Error('Require login page.'));
					}
					oidcUser = user;
					return {
						status: 'logged-in'
					};
				})
				.catch(function () {
					// There are no active sessions so signin
					return oidcUserManager.signinRedirect(options).then(function () {
						return {
							status: 'in-progress'
						};
					});
				});
		},

		relogin: function (options) {
			// eslint-disable-next-line @typescript-eslint/no-this-alias
			const self = this;
			options = this._convertOptionsToOidcSettings(options);
			let silentPromise;
			if (
				options.prompt === 'login' ||
				options.prompt === 'select_account' ||
				!this._isSilentRenewActive()
			) {
				silentPromise = Promise.reject(new Error('Require login page.'));
			} else {
				// Try update session with silent login.
				silentPromise = oidcUserManager.signinSilent();
			}
			return silentPromise
				.catch(function () {
					if (self.popupPromise) {
						return self.popupPromise;
					}
					self.popupPromise = oidcUserManager.signinPopup(options).then(
						function (user) {
							self.popupPromise = null;
							return user;
						},
						function (err) {
							self.popupPromise = null;
							return Promise.reject(err);
						}
					);
					return self.popupPromise;
				})
				.then(function (user) {
					oidcUser = user;
				});
		},

		logout: function (options) {
			return oidcUserManager
				.clearStaleState()
				.catch(this._catchError) // Continue signout in any case
				.then(function () {
					return oidcUserManager.signoutRedirect(options).then(function () {
						oidcUser = null;
					});
				});
		},

		isLogged: function () {
			return !!oidcUser;
		},

		getToken: function () {
			this._ensureUserLoggedIn();
			return oidcUser.access_token;
		},

		get unauthorizedStatusCode() {
			return protocolInfo.unauthorized_status_code;
		},

		get authorizationHeaderName() {
			return protocolInfo.authorization_header;
		},

		getAuthorizationHeader: function () {
			const headerObject = {};
			const headerName = this.authorizationHeaderName;
			if (headerName && oidcUser) {
				headerObject[headerName] = this._getTokenType() + ' ' + this.getToken();
			}

			return headerObject;
		},

		_convertOptionsToOidcSettings: function (options) {
			const oidcOptions = {
				extraQueryParams: {}
			};
			options = options || {};
			this._assignProperty('prompt', options, oidcOptions);
			this._assignProperty('login_hint', options, oidcOptions);
			this._assignProperty('database', options, oidcOptions.extraQueryParams);
			this._assignProperty(
				'authentication_type',
				options,
				oidcOptions.extraQueryParams
			);
			this._assignProperty('state', options, oidcOptions);
			return oidcOptions;
		},

		_assignProperty: function (propertyName, fromObject, toObject) {
			if (fromObject[propertyName]) {
				toObject[propertyName] = fromObject[propertyName];
			}
		},

		_catchError: function (e) {
			oidc.Logger.error('Unhandled error: ' + e);
		},

		_ensureUserLoggedIn: function () {
			if (!this.isLogged()) {
				throw new Error('You are not logged in!');
			}
		},

		_getTokenType: function () {
			this._ensureUserLoggedIn();
			return oidcUser.token_type;
		},

		_onUserLoaded: function (loadedUser) {
			oidcUserManager.startSilentRenew();
			oidcUser = loadedUser;
		},

		_onUserUnloaded: function () {
			oidcUserManager.stopSilentRenew();
		},

		_onUserSignedOut: function () {
			// Stop silent renew when user logged out
			// because it may login again under another user
			// and during silent renew we may load access token for another user
			// but current UI still is for the initial user.
			oidcUserManager.stopSilentRenew();
		},

		_isSilentRenewActive: function () {
			return !!oidcUserManager._silentRenewService._isStarted;
		}
	};
})(window);
