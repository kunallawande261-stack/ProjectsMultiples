// eslint-disable-next-line
// @ts-nocheck
(function (wnd) {
	const SyncPromise = ArasModules.SyncPromise;

	const SECOND = 1000;
	const MINUTE = 60 * 1000;

	const storage = {
		oauthClientId: null,
		tokenType: null,
		accessToken: null,
		refreshToken: null,
		expiresIn: null,
		authorizationHeaderName: null,
		unauthorizedStatusCode: null,
		tokenEndpoint: null,
		accessTokenExpiresAt: null,

		grantType: null,
		database: null,
		scriptsUrl: null,

		tokenRenewPromise: null
	};

	const discoveryPath = 'OAuthServerDiscovery.aspx';
	const configurationPath = '.well-known/openid-configuration';

	const discoveryErrorMessage =
		'Cannot access OAuth Server due to incorrect Discovery configuration';
	const httpErrorMessage = 'Cannot access OAuth Server due to {0} ({1})';
	const networkErrorMessage = 'Cannot access OAuth Server: Network Error';

	const lastAccessibleOAuthServerUrlKey = 'lastAccessibleOAuthServerUrl';

	wnd.OAuthClientPasswordGrant = {
		get unauthorizedStatusCode() {
			return storage.unauthorizedStatusCode;
		},

		get authorizationHeaderName() {
			return storage.authorizationHeaderName;
		},

		init: function (oauthClientId, baseServerUrl) {
			storage.oauthClientId = oauthClientId;
			// eslint-disable-next-line @typescript-eslint/no-this-alias
			const self = this;
			return this._getOAuthServerUrls(baseServerUrl).then(function (
				oauthServerUrls
			) {
				// Optimization by host
				const priorityHost = self._getHost(baseServerUrl);
				oauthServerUrls = self._prioritizeHost(oauthServerUrls, priorityHost);

				// Optimization by last accessible url
				const storage = self._getStorage();
				const priorityUrl = storage.getItem(lastAccessibleOAuthServerUrlKey);
				if (priorityUrl) {
					oauthServerUrls = self._prioritizeUrl(oauthServerUrls, priorityUrl);
				}

				const errorInfos = [];
				let promise = oauthServerUrls.reduce(function (
					promise,
					oauthServerUrl
				) {
					return promise.catch(function () {
						return self
							._getConfiguration(oauthServerUrl)
							.then(function (configuration) {
								storage.setItem(
									lastAccessibleOAuthServerUrlKey,
									oauthServerUrl
								);
								return configuration;
							})
							.catch(function (error) {
								errorInfos.push({
									error: error,
									url: oauthServerUrl
								});
								return SyncPromise.reject(error);
							});
					});
				},
				SyncPromise.reject());
				promise = promise.catch(function (lastError) {
					return SyncPromise.reject(
						oauthServerUrls.length == 1
							? lastError
							: self._combineErrorInfos(errorInfos)
					);
				});
				return promise;
			});
		},

		login: function (grantType, options) {
			storage.grantType = grantType;

			if (grantType === 'passwordGrant') {
				return this._requestPasswordGrant(
					options.loginName,
					options.hashPassword,
					options.database
				);
			} else {
				return SyncPromise.reject(
					new Error('Not supported grant type: ' + grantType)
				);
			}
		},

		getToken: function () {
			const timeLeft = storage.accessTokenExpiresAt - Date.now();
			const accessTokenLifetimeRelaxBorder = 10 * MINUTE;
			const accessTokenLifetimeLastChanceBorder = 5 * MINUTE;

			if (
				timeLeft >= accessTokenLifetimeLastChanceBorder &&
				timeLeft < accessTokenLifetimeRelaxBorder
			) {
				this._startRenewTokenCycle(true);
			} else if (timeLeft < accessTokenLifetimeLastChanceBorder) {
				this._startRenewTokenCycle(false).catch(function (error) {
					return SyncPromise.reject(error);
				});
			}
			return storage.accessToken;
		},

		getAuthorizationHeader: function () {
			const headerObject = {};
			const headerName = this.authorizationHeaderName;
			if (headerName) {
				headerObject[headerName] = storage.tokenType + ' ' + this.getToken();
			}

			return headerObject;
		},

		_setAuthorizationData: function (
			requestHaveBeenSentTimestamp,
			authorizationData
		) {
			storage.tokenType = authorizationData.token_type;
			storage.accessToken = authorizationData.access_token;
			storage.refreshToken = authorizationData.refresh_token;
			storage.expiresIn = authorizationData.expires_in;

			// server returns value in seconds
			storage.accessTokenExpiresAt =
				requestHaveBeenSentTimestamp + authorizationData.expires_in * SECOND;
		},

		_getOAuthServerUrls: function (baseServerUrl) {
			const discoveryUrl = baseServerUrl + discoveryPath;
			const options = {
				method: 'GET',
				url: discoveryUrl,
				withCredentials: true
			};

			return this._sendRequest(null, options)
				.then(function (response) {
					return response ? JSON.parse(response) : response;
				})
				.then(function (discoveryJson) {
					if (
						!discoveryJson ||
						!Array.isArray(discoveryJson.locations) ||
						!discoveryJson.locations.length
					) {
						return SyncPromise.reject(new Error(discoveryErrorMessage));
					}

					const oauthServerUrls = discoveryJson.locations.map(function (
						location
					) {
						return location.uri;
					});
					return oauthServerUrls;
				});
		},

		_getConfigurationEndpoint: function (oauthServerUrl) {
			oauthServerUrl = oauthServerUrl.endsWith('/')
				? oauthServerUrl
				: oauthServerUrl + '/';
			const configurationEndpoint = oauthServerUrl + configurationPath;
			return configurationEndpoint;
		},

		_getConfiguration: function (oauthServerUrl) {
			const configurationEndpoint =
				this._getConfigurationEndpoint(oauthServerUrl);
			const options = {
				method: 'GET',
				url: configurationEndpoint,
				withCredentials: true
			};
			return this._sendRequest(null, options)
				.then(function (response) {
					return response ? JSON.parse(response) : response;
				})
				.then(function (configuration) {
					storage.tokenEndpoint = configuration.token_endpoint;
					storage.authorizationHeaderName =
						configuration.protocol_info.authorization_header;
					storage.unauthorizedStatusCode =
						configuration.protocol_info.unauthorized_status_code;
				})
				.catch(function (error) {
					if (error.constructor === XMLHttpRequest) {
						let errorMessage;
						if (error.status !== 0 && error.statusText) {
							errorMessage = httpErrorMessage
								.replace('{0}', error.status)
								.replace('{1}', error.statusText);
						} else {
							errorMessage = networkErrorMessage;
						}

						error = new Error(errorMessage);
					}

					return SyncPromise.reject(error);
				});
		},

		_combineErrorInfos: function (errorInfos) {
			return errorInfos.reduce(function (message, errorInfo) {
				const errorMessage = errorInfo.error;
				const url = errorInfo.url;
				if (message.length) {
					message = message + '\n\n';
				}
				return message + errorMessage + '\n' + '  from ' + url;
			}, '');
		},

		_requestPasswordGrant: function (loginName, hashPassword, database) {
			// eslint-disable-next-line @typescript-eslint/no-this-alias
			const self = this;

			const data = new URLSearchParams();
			data.append('grant_type', 'password');
			data.append('scope', 'Innovator offline_access');
			data.append('username', loginName);
			data.append('password', hashPassword);
			data.append('database', database);
			data.append('client_id', storage.oauthClientId);

			const options = {
				url: storage.tokenEndpoint,
				method: 'POST',
				headers: {
					'Content-type': 'application/x-www-form-urlencoded; charset=utf-8'
				},
				withCredentials: true
			};

			const requestHaveBeenSentTimestamp = Date.now();

			return this._sendRequest(data.toString(), options)
				.then(function (response) {
					return response ? JSON.parse(response) : response;
				})
				.then(function (data) {
					if (data.error) {
						return SyncPromise.reject(data);
					}

					self._setAuthorizationData(requestHaveBeenSentTimestamp, data);
				})
				.catch(function (error) {
					if (error.constructor === XMLHttpRequest && error.responseText) {
						return SyncPromise.reject(new Error(error.responseText));
					}
					return SyncPromise.reject(error);
				});
		},

		_sendRequest: function (data, options) {
			const req = new XMLHttpRequest();
			req.open(options.method, options.url, false);

			if (options.headers) {
				Object.keys(options.headers).forEach(function (key) {
					req.setRequestHeader(key, options.headers[key]);
				});
			}

			if (options.withCredentials) {
				req.withCredentials = true;
			}

			const promise = new SyncPromise(function (resolve, reject) {
				try {
					req.send(options.method.toLowerCase() === 'get' ? null : data);
				} catch (e) {
					return reject(req);
				}

				if (req.status === 200) {
					resolve(req.responseText);
				} else {
					reject(req);
				}
			});
			promise.abort = this.abort(req);
			return promise;
		},

		abort: function (xhr) {
			return function () {
				// eslint-disable-next-line @typescript-eslint/no-empty-function
				xhr.onpropertychange = function () {};
				xhr.abort();
			};
		},

		_startRenewTokenCycle: function (isAsync) {
			// eslint-disable-next-line @typescript-eslint/no-this-alias
			const self = this;
			const isTokenRenewInProgress = !!this._tokenRenewPromise;

			if (!isAsync) {
				return isTokenRenewInProgress
					? SyncPromise.resolve()
					: this._renewToken(isAsync);
			}

			if (!isTokenRenewInProgress) {
				this._tokenRenewPromise = this._renewToken(isAsync);
			}

			return this._tokenRenewPromise.then(function () {
				self._tokenRenewPromise = null;
			});
		},

		_renewToken: function (async) {
			// eslint-disable-next-line @typescript-eslint/no-this-alias
			const self = this;
			const promiseClass = async ? Promise : SyncPromise;

			const refreshToken = storage.refreshToken;
			const oauthClientId = storage.oauthClientId;

			if (!refreshToken) {
				return promiseClass.reject();
			}

			const data = new URLSearchParams();
			data.append('grant_type', 'refresh_token');
			data.append('refresh_token', refreshToken);
			data.append('client_id', oauthClientId);
			const body = data.toString();

			const headers = {
				'Content-type': 'application/x-www-form-urlencoded; charset=utf-8'
			};

			const requestHaveBeenSentTimestamp = Date.now();

			return (
				async
					? this._renewTokenAsync(headers, body)
					: this._renewTokenSync(headers, body)
			)
				.then(function (response) {
					if (!response || response.error) {
						return promiseClass.reject(response);
					}

					self._setAuthorizationData(requestHaveBeenSentTimestamp, response);
				})
				.catch(function (error) {
					storage.refreshToken = null;
					return promiseClass.reject(error);
				});
		},

		_renewTokenSync: function (headers, body) {
			const xhr = new XMLHttpRequest();
			xhr.open('POST', storage.tokenEndpoint, false);

			const keys = Object.keys(headers);
			keys.forEach(function (key) {
				xhr.setRequestHeader(key, headers[key]);
			});
			xhr.withCredentials = true;
			xhr.send(body);

			const responseData = JSON.parse(xhr.responseText);
			if (xhr.status === 200) {
				return SyncPromise.resolve(responseData);
			}
			return SyncPromise.reject(responseData);
		},

		_renewTokenAsync: function (headers, body) {
			const params = {
				method: 'POST',
				headers: headers,
				body: body,
				credentials: 'include'
			};

			return fetch(storage.tokenEndpoint, params)
				.then(function (response) {
					return response.json();
				})
				.then(function (data) {
					if (data.error) {
						return Promise.reject(data);
					}
					return Promise.resolve(data);
				});
		},

		_getHost: function (url) {
			const location = new URL(url);
			return location.hostname;
		},

		_prioritizeHost: function (oauthServerUrls, priorityHost) {
			// eslint-disable-next-line @typescript-eslint/no-this-alias
			const self = this;
			const urlsWithPriorityHost = [];
			const urlsWithoutPriorityHost = [];
			oauthServerUrls.forEach(function (oauthServerUrl) {
				const host = self._getHost(oauthServerUrl);
				if (host === priorityHost) {
					urlsWithPriorityHost.push(oauthServerUrl);
				} else {
					urlsWithoutPriorityHost.push(oauthServerUrl);
				}
			});
			return urlsWithPriorityHost.concat(urlsWithoutPriorityHost);
		},

		_prioritizeUrl: function (oauthServerUrls, priorityUrl) {
			priorityUrl = priorityUrl.toLowerCase();
			const urlsWithPriorityUrl = [];
			const urlsWithoutPriorityUrl = [];
			oauthServerUrls.forEach(function (oauthServerUrl) {
				if (oauthServerUrl.toLowerCase() === priorityUrl) {
					urlsWithPriorityUrl.push(oauthServerUrl);
				} else {
					urlsWithoutPriorityUrl.push(oauthServerUrl);
				}
			});
			return urlsWithPriorityUrl.concat(urlsWithoutPriorityUrl);
		},

		_getStorage: function () {
			return localStorage;
		}
	};
})(window);
