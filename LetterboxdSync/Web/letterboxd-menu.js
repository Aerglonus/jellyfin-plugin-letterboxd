/**
 * Letterboxd Sync - Menu Integration Script
 *
 * This script injects Letterboxd links into the Jellyfin sidebar and
 * My Preferences menu so that all users (not just admins) can access
 * their Letterboxd settings and sync stats.
 *
 * To use: Load this script via your JavaScript Injector plugin or
 * paste it into the custom JS configuration field.
 *
 * Jellyfin sidebar structure (from libraryMenu.js):
 *   .mainDrawer > [navDrawerScrollContainer]
 *     a.navMenuOption.lnkMediaFolder  (Home)
 *     .customMenuOptions              (custom menu links from config.json)
 *     .libraryMenuOptions             (library folders)
 *     .adminMenuOptions               (Dashboard, Metadata - admin only)
 *     .userMenuOptions                (Settings, Sign Out - all users)
 */
(function () {
	'use strict';

	// Fallback base URLs if ApiClient is not yet loaded
	var baseUrl = window.location.origin;
	var webPath = window.location.pathname
		.replace(/\/web\/.*$/, '')
		.replace(/\/web\/?$/, '');
	var SETTINGS_URL =
		webPath + '/Jellyfin.Plugin.LetterboxdSync/UserSettingsPage';
	var STATS_URL = webPath + '/Jellyfin.Plugin.LetterboxdSync/StatsPage';

	var INJECTED_ATTR = 'data-letterboxd-injected';
	var STYLE_ID = 'letterboxd-menu-styles';

	function ensureMenuStyles() {
		if (document.getElementById(STYLE_ID)) {
			return;
		}

		var style = document.createElement('style');
		style.id = STYLE_ID;
		style.textContent =
			'.mainDrawer-scrollContainer .pluginMenuOptions[' +
			INJECTED_ATTR +
			'] {' +
			'margin:.5em 0 .75em;' +
			'}' +
			'.mainDrawer-scrollContainer .pluginMenuOptions[' +
			INJECTED_ATTR +
			'] .navMenuOptionGroupTitle {' +
			'margin:1.25em 1.9em .35em;' +
			'font-size:.78em;' +
			'font-weight:600;' +
			'line-height:1.3;' +
			'opacity:.7;' +
			'}' +
			'.lb-preferences-section[' +
			INJECTED_ATTR +
			'] {' +
			'margin-top:1em;' +
			'}' +
			'.lb-preferences-section[' +
			INJECTED_ATTR +
			'] .sectionTitle {' +
			'margin-bottom:.45em;' +
			'padding-left:0;' +
			'}' +
			'.lb-preferences-section[' +
			INJECTED_ATTR +
			'] .listItem {' +
			'box-sizing:border-box;' +
			'color:inherit;' +
			'text-decoration:none;' +
			'}' +
			'.lb-preferences-section[' +
			INJECTED_ATTR +
			'] .listItemBody {' +
			'min-width:0;' +
			'}' +
			'.lb-preferences-section[' +
			INJECTED_ATTR +
			'] .listItemBodyText {' +
			'overflow:hidden;' +
			'text-overflow:ellipsis;' +
			'white-space:nowrap;' +
			'}' +
			'.lb-preferences-section[' +
			INJECTED_ATTR +
			'] .listItemBodyText.secondary {' +
			'opacity:.7;' +
			'}';

		document.head.appendChild(style);
	}

	function getApiClient() {
		return typeof ApiClient !== 'undefined' ? ApiClient : null;
	}

	function getPluginUrl(route, fallbackUrl) {
		var apiClient = getApiClient();
		if (apiClient && typeof apiClient.getUrl === 'function') {
			return apiClient.getUrl('Jellyfin.Plugin.LetterboxdSync/' + route);
		}

		return fallbackUrl;
	}

	function getAuthSuffix() {
		var apiClient = getApiClient();
		var server = '';
		var token = '';

		if (apiClient) {
			if (typeof apiClient.serverAddress === 'function') {
				server = apiClient.serverAddress() || '';
			}

			if (typeof apiClient.accessToken === 'function') {
				token = apiClient.accessToken() || '';
			}
		}

		return (
			'?server=' +
			encodeURIComponent(server) +
			'&token=' +
			encodeURIComponent(token)
		);
	}

	function createNavLink(href, iconName, label, extraClass) {
		var a = document.createElement('a');
		a.setAttribute('is', 'emby-linkbutton');
		a.className =
			'navMenuOption lnkMediaFolder emby-button ' + (extraClass || '');
		setPluginLinkTarget(a, href);
		a.setAttribute(INJECTED_ATTR, 'true');

		var icon = document.createElement('span');
		icon.className = 'material-icons navMenuOptionIcon ' + iconName;
		icon.setAttribute('aria-hidden', 'true');
		a.appendChild(icon);

		var text = document.createElement('span');
		text.className = 'navMenuOptionText';
		text.textContent = label;
		a.appendChild(text);

		return a;
	}

	function setPluginLinkTarget(link, href) {
		link.href = '#';
		link.setAttribute('data-plugin-url', href);
		if (!link.getAttribute('data-plugin-click-bound')) {
			link.addEventListener(
				'click',
				function (event) {
					var pluginUrl = link.getAttribute('data-plugin-url');
					if (!pluginUrl) {
						return;
					}

					event.preventDefault();
					event.stopPropagation();
					event.stopImmediatePropagation();
					window.location.assign(pluginUrl);
				},
				true,
			);
			link.setAttribute('data-plugin-click-bound', 'true');
		}

		link.onclick = function (event) {
			var pluginUrl = link.getAttribute('data-plugin-url') || href;
			event.preventDefault();
			event.stopPropagation();
			window.location.assign(pluginUrl);
			return false;
		};
	}

	function upsertNavLink(container, href, iconName, label, linkClass) {
		var link = container.querySelector('.' + linkClass);
		if (!link) {
			link = createNavLink(
				href,
				iconName,
				label,
				'lb-injected-link ' + linkClass,
			);
			container.appendChild(link);
			return;
		}

		setPluginLinkTarget(link, href);

		var icon = link.querySelector('.navMenuOptionIcon');
		if (icon) {
			icon.className = 'material-icons navMenuOptionIcon ' + iconName;
			icon.textContent = '';
		}

		var text = link.querySelector('.navMenuOptionText');
		if (text) {
			text.textContent = label;
		}
	}

	function injectSidebar() {
		var scrollContainer = document.querySelector('.mainDrawer-scrollContainer');
		if (!scrollContainer) return;

		var suffix = getAuthSuffix();
		var settingsUrl = getPluginUrl('UserSettingsPage', SETTINGS_URL);
		var statsUrl = getPluginUrl('StatsPage', STATS_URL);

		var pluginMenuOptions = scrollContainer.querySelector('.pluginMenuOptions');
		if (!pluginMenuOptions) {
			pluginMenuOptions = document.createElement('div');
			pluginMenuOptions.className = 'navMenuOptionGroup pluginMenuOptions';
			pluginMenuOptions.setAttribute(INJECTED_ATTR, 'true');

			var header = document.createElement('h2');
			header.className = 'navMenuOptionGroupTitle';
			header.textContent = 'Plugins';
			pluginMenuOptions.appendChild(header);

			// Insert after userMenuOptions or adminMenuOptions
			var userMenuOptions = scrollContainer.querySelector('.userMenuOptions');
			var adminMenuOptions = scrollContainer.querySelector('.adminMenuOptions');
			var insertAfter = userMenuOptions || adminMenuOptions;

			if (insertAfter && insertAfter.parentNode === scrollContainer) {
				scrollContainer.insertBefore(
					pluginMenuOptions,
					insertAfter.nextSibling,
				);
			} else {
				scrollContainer.appendChild(pluginMenuOptions);
			}
		}

		upsertNavLink(
			pluginMenuOptions,
			settingsUrl + suffix,
			'settings',
			'Letterboxd',
			'lb-settings-link',
		);
		upsertNavLink(
			pluginMenuOptions,
			statsUrl + suffix,
			'analytics',
			'Letterboxd Stats',
			'lb-stats-link',
		);
	}

	function injectPreferencesMenu() {
		// Only run on the My Preferences page
		if (window.location.href.indexOf('mypreferencesmenu') === -1) {
			return;
		}

		// The preferences page uses .listItem elements inside a verticalSection
		var sections = document.querySelectorAll('.verticalSection');
		if (!sections.length) return;

		// Find the section that contains the preference links
		var prefSection = null;
		for (var i = 0; i < sections.length; i++) {
			if (
				sections[i].querySelector('.listItem') ||
				sections[i].querySelector('[is="emby-button"]')
			) {
				prefSection = sections[i];
				break;
			}
		}

		if (!prefSection) return;

		var suffix = getAuthSuffix();
		var settingsUrl = getPluginUrl('UserSettingsPage', SETTINGS_URL);
		var statsUrl = getPluginUrl('StatsPage', STATS_URL);

		var existingSection = prefSection.parentNode.querySelector(
			'.lb-preferences-section[' + INJECTED_ATTR + ']',
		);
		if (existingSection) {
			var existingSettings = existingSection.querySelector('.lb-settings-item');
			var existingStats = existingSection.querySelector('.lb-stats-item');

			if (existingSettings) {
				setPluginLinkTarget(existingSettings, settingsUrl + suffix);
				var existingSettingsIcon =
					existingSettings.querySelector('.listItemIcon');
				if (existingSettingsIcon) {
					existingSettingsIcon.className =
						'material-icons listItemIcon settings';
					existingSettingsIcon.textContent = '';
				}
			}

			if (existingStats) {
				setPluginLinkTarget(existingStats, statsUrl + suffix);
				var existingStatsIcon = existingStats.querySelector('.listItemIcon');
				if (existingStatsIcon) {
					existingStatsIcon.className =
						'material-icons listItemIcon analytics';
					existingStatsIcon.textContent = '';
				}
			}

			return;
		}

		// Create Letterboxd preference items matching Jellyfin's style
		var container = document.createElement('div');
		container.className = 'verticalSection lb-preferences-section';
		container.setAttribute(INJECTED_ATTR, 'true');

		var header = document.createElement('h2');
		header.className = 'sectionTitle sectionTitle-cards';
		header.textContent = 'Letterboxd';
		container.appendChild(header);

		var settingsItem = document.createElement('a');
		settingsItem.className = 'listItem listItem-border lb-settings-item';
		setPluginLinkTarget(settingsItem, settingsUrl + suffix);
		settingsItem.setAttribute(INJECTED_ATTR, 'true');
		settingsItem.innerHTML =
			'<span class="material-icons listItemIcon settings" aria-hidden="true"></span>' +
			'<div class="listItemBody">' +
			'<div class="listItemBodyText">Letterboxd Settings</div>' +
			'<div class="listItemBodyText secondary">Configure your Letterboxd account and sync preferences</div>' +
			'</div>' +
			'<span class="material-icons listItemAside chevron_right" aria-hidden="true"></span>';
		container.appendChild(settingsItem);

		var statsItem = document.createElement('a');
		statsItem.className = 'listItem listItem-border lb-stats-item';
		setPluginLinkTarget(statsItem, statsUrl + suffix);
		statsItem.setAttribute(INJECTED_ATTR, 'true');
		statsItem.innerHTML =
			'<span class="material-icons listItemIcon analytics" aria-hidden="true"></span>' +
			'<div class="listItemBody">' +
			'<div class="listItemBodyText">Letterboxd Stats</div>' +
			'<div class="listItemBodyText secondary">View sync history and statistics</div>' +
			'</div>' +
			'<span class="material-icons listItemAside chevron_right" aria-hidden="true"></span>';
		container.appendChild(statsItem);

		// Insert after the last existing section
		prefSection.parentNode.insertBefore(container, prefSection.nextSibling);
	}

	function runInjections() {
		ensureMenuStyles();
		injectSidebar();
		injectPreferencesMenu();
	}

	// Use MutationObserver to handle Jellyfin's SPA navigation
	var observer = new MutationObserver(function () {
		// Debounce to avoid excessive calls
		clearTimeout(observer._timer);
		observer._timer = setTimeout(runInjections, 200);
	});

	if (document.body) {
		observer.observe(document.body, { childList: true, subtree: true });
	}

	// Also listen for hash changes (SPA navigation)
	window.addEventListener('hashchange', function () {
		setTimeout(runInjections, 500);
	});

	window.addEventListener('popstate', function () {
		setTimeout(runInjections, 500);
	});

	document.addEventListener('viewshow', runInjections);
	document.addEventListener('DOMContentLoaded', runInjections);

	// Initial run
	runInjections();

	console.log('[LetterboxdSync] Menu integration loaded');
})();
