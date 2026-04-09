// Copyright (C) Microsoft Corporation. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

#ifndef __core_webview2_experimental_environment_options_h__
#define __core_webview2_experimental_environment_options_h__

#include <objbase.h>
#include <wrl/implements.h>

#include "WebView2EnvironmentOptions.h"
#include "webview2experimental.h"

// This is a base COM class that implements IUnknown if there is no Experimental
// options, or ICoreWebView2ExperimentalEnvironmentOptions when there are
// Experimental options.
template <typename allocate_fn_t,
          allocate_fn_t allocate_fn,
          typename deallocate_fn_t,
          deallocate_fn_t deallocate_fn>
class CoreWebView2ExperimentalEnvironmentOptionsBase
    : public Microsoft::WRL::Implements<
          Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
          ICoreWebView2ExperimentalEnvironmentOptions> {
 public:
  static const COREWEBVIEW2_RELEASE_CHANNELS kInternalChannel =
      static_cast<COREWEBVIEW2_RELEASE_CHANNELS>(1 << 4);
  static const COREWEBVIEW2_RELEASE_CHANNELS kAllChannels =
      COREWEBVIEW2_RELEASE_CHANNELS_STABLE |
      COREWEBVIEW2_RELEASE_CHANNELS_BETA | COREWEBVIEW2_RELEASE_CHANNELS_DEV |
      COREWEBVIEW2_RELEASE_CHANNELS_CANARY | kInternalChannel;

  CoreWebView2ExperimentalEnvironmentOptionsBase() {}

  // ICoreWebView2StagingEnvironmentOptions
  HRESULT STDMETHODCALLTYPE
  get_ReleaseChannels(COREWEBVIEW2_RELEASE_CHANNELS* channels) override {
    if (!channels) {
      return E_POINTER;
    }
    *channels = m_releaseChannels;
    return S_OK;
  }

  HRESULT STDMETHODCALLTYPE
  put_ReleaseChannels(COREWEBVIEW2_RELEASE_CHANNELS channels) override {
    m_releaseChannels = channels;
    return S_OK;
  }

  // ICoreWebView2ExperimentalEnvironmentOptions
  HRESULT STDMETHODCALLTYPE
  get_ChannelSearchKind(COREWEBVIEW2_CHANNEL_SEARCH_KIND* value) override {
    if (!value) {
      return E_POINTER;
    }
    *value = m_channelSearchKind;
    return S_OK;
  }

  HRESULT STDMETHODCALLTYPE
  put_ChannelSearchKind(COREWEBVIEW2_CHANNEL_SEARCH_KIND value) override {
    m_channelSearchKind = value;
    return S_OK;
  }

 protected:
  ~CoreWebView2ExperimentalEnvironmentOptionsBase() = default;

 private:
  COREWEBVIEW2_RELEASE_CHANNELS m_releaseChannels = kAllChannels;
  COREWEBVIEW2_CHANNEL_SEARCH_KIND m_channelSearchKind =
      COREWEBVIEW2_CHANNEL_SEARCH_KIND_MOST_STABLE;
};

template <typename allocate_fn_t,
          allocate_fn_t allocate_fn,
          typename deallocate_fn_t,
          deallocate_fn_t deallocate_fn>
class CoreWebView2ExperimentalEnvironmentOptionsClass
    : public Microsoft::WRL::RuntimeClass<
          Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
          CoreWebView2EnvironmentOptionsBase<allocate_fn_t,
                                             allocate_fn,
                                             deallocate_fn_t,
                                             deallocate_fn>,
          CoreWebView2ExperimentalEnvironmentOptionsBase<allocate_fn_t,
                                                         allocate_fn,
                                                         deallocate_fn_t,
                                                         deallocate_fn>> {
 public:
  CoreWebView2ExperimentalEnvironmentOptionsClass() {}

  ~CoreWebView2ExperimentalEnvironmentOptionsClass() override{};
};

typedef CoreWebView2ExperimentalEnvironmentOptionsClass<
    decltype(&::CoTaskMemAlloc),
    ::CoTaskMemAlloc,
    decltype(&::CoTaskMemFree),
    ::CoTaskMemFree>
    CoreWebView2ExperimentalEnvironmentOptions;

#endif  // __core_webview2_experimental_environment_options_h__
