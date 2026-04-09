// eslint-disable-next-line
// @ts-nocheck
(function (global) {
	const discoveryPath = 'OAuthServerDiscovery.aspx';
	const configurationPath = '.well-known/openid-configuration';

	const lastAccessibleOAuthServerUrlKey = 'lastAccessibleOAuthServerUrl';

	global.OAuthServerDiscovery = {
		discover: function (baseServerUrl) {
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
								return Promise.reject(error);
							});
					});
				},
				Promise.reject(
					new Error('Initial reject for running looped operations.')
				));
				promise = promise.catch(function (lastError) {
					return Promise.reject(
						oauthServerUrls.length == 1
							? lastError
							: self._combineErrorInfos(errorInfos)
					);
				});
				return promise;
			});
		},

		_getOAuthServerUrls: function (baseServerUrl) {
			const discoveryUrl = baseServerUrl + discoveryPath;

			return fetch(discoveryUrl, { method: 'GET', credentials: 'same-origin' })
				.then(function (response) {
					if (response.status !== 200) {
						return Promise.reject(
							new Error(
								`Innovator Server responded with ${response.status} (${response.statusText}): cannot get OAuth Server urls from ${discoveryUrl}. \nPlease check Innovator Server logs`
							)
						);
					}

					return response.json();
				})
				.then(function (discoveryJson) {
					if (
						!discoveryJson ||
						!Array.isArray(discoveryJson.locations) ||
						!discoveryJson.locations.length
					) {
						return Promise.reject(
							new Error(
								'Cannot access OAuth Server due to incorrect Discovery configuration'
							)
						);
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
			return fetch(configurationEndpoint, {
				method: 'GET',
				credentials: 'include'
			})
				.then(function (response) {
					if (response.status !== 200) {
						return Promise.reject(
							new Error(
								`Cannot access OAuth Server due to ${response.status} (${response.statusText})`
							)
						);
					}

					return response.json();
				})
				.catch(function (error) {
					// There are no chances to differ Network error from CORS error:
					// See "How to Detect Cross Origin (CORS) Error vs. Other Types of Errors for XMLHttpRequest() in Javascript"
					// https://stackoverflow.com/a/19325710
					const polyfillCorsErrorMessage = 'Network request failed';
					const firefoxCorsErrorMessage =
						'NetworkError when attempting to fetch resource.';
					const defaultCorsErrorMessage = 'Failed to fetch'; // Chrome, Edge

					const isCorsError =
						(error &&
							error.message &&
							error.message === polyfillCorsErrorMessage) ||
						error.message === firefoxCorsErrorMessage ||
						error.message === defaultCorsErrorMessage;

					if (isCorsError) {
						return Promise.reject(
							new Error('Cannot access OAuth Server due to CORS policies')
						);
					}

					return Promise.reject(error);
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
