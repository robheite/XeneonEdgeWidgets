/******************************************************************************
 **
 ** File      ticker-tracker.js
 ** Author    Maksym Aldokhin
 ** Copyright (c) 2026, Corsair Memory, Inc. All Rights Reserved.
 **
 ** This file is part of Corsair iCUE Software.
 **
 ******************************************************************************/

window.TickerTracker = (function () {
	"use strict";

	let ticker = null;
	let track = null;
	let textEl = null;
	let isInitialized = false;
	let resizeTimeout = null;

	const SETUP_DELAY = 100;
	const RESIZE_DEBOUNCE = 200;
	const TEXT_UPDATE_DELAY = 50;

	function debounce(fn, wait) {
		let timeout;
		return function executedFunction(...args) {
			const later = () => {
				clearTimeout(timeout);
				fn(...args);
			};
			clearTimeout(timeout);
			timeout = setTimeout(later, wait);
		};
	}

	function setupTicker() {
		// Remove any existing clones
		while (track.children.length > 1) {
			track.removeChild(track.lastChild);
		}

		// Force a reflow to get accurate measurements
		ticker.style.visibility = "hidden";
		ticker.offsetHeight; // Force reflow
		ticker.style.visibility = "visible";

		const containerWidth = Math.ceil(ticker.clientWidth);
		const textWidth = Math.ceil(textEl.scrollWidth);

		if (textWidth <= containerWidth) {
			// Text fits, no scrolling needed
			ticker.classList.remove("is-scrolling");
			ticker.classList.add("not-scrolling");
			track.style.removeProperty("--duration");
			track.style.removeProperty("--shift");
			track.style.justifyContent = "center";
			return;
		}

		ticker.classList.remove("not-scrolling");
		ticker.classList.add("is-scrolling");
		track.style.removeProperty("justify-content");

		const clone = textEl.cloneNode(true);
		track.appendChild(clone);

		const gap = parseInt(getComputedStyle(track).getPropertyValue("--gap")) || 32;
		const shift = textWidth + gap;

		const fontSize = parseFloat(getComputedStyle(textEl).fontSize);

		const k = 1.5; // coefficient text height to pixels per second

		const speedPxPerSec = fontSize * k;
		const durationSec = shift / speedPxPerSec;

		track.style.setProperty("--shift", shift + "px");
		track.style.setProperty("--duration", durationSec + "s");
	}

	function init(tickerId, trackId, textId) {
		ticker = document.getElementById(tickerId);
		track = document.getElementById(trackId);
		textEl = document.getElementById(textId);

		if (!ticker || !track || !textEl) {
			return false;
		}

		const debouncedSetup = debounce(setupTicker, RESIZE_DEBOUNCE);

		window.addEventListener("load", () => {
			setTimeout(setupTicker, SETUP_DELAY);
		});

		window.addEventListener("resize", debouncedSetup);

		isInitialized = true;

		if (document.readyState === "complete") {
			setTimeout(setupTicker, SETUP_DELAY);
		}

		return true;
	}

	function setText(newText) {
		if (!isInitialized || !textEl) {
			return false;
		}

		if (textEl.textContent !== newText) {
			textEl.textContent = newText;
			// Use setTimeout to ensure DOM updates before recalculating
			setTimeout(setupTicker, TEXT_UPDATE_DELAY);
		}

		return true;
	}

	function getText() {
		if (!isInitialized || !textEl) {
			return "";
		}

		return textEl.textContent;
	}

	function recalculate() {
		if (!isInitialized) {
			return false;
		}

		setupTicker();
		return true;
	}

	function destroy() {
		if (!isInitialized) {
			return false;
		}

		window.removeEventListener("load", setupTicker);
		window.removeEventListener("resize", debounce(setupTicker, RESIZE_DEBOUNCE));

		if (resizeTimeout) {
			clearTimeout(resizeTimeout);
			resizeTimeout = null;
		}

		ticker = null;
		track = null;
		textEl = null;
		isInitialized = false;

		return true;
	}

	function isReady() {
		return isInitialized && ticker && track && textEl;
	}

	function setVerticalAlign(alignment) {
		if (!isInitialized || !ticker) {
			return false;
		}

		let alignValue;

		// Handle keyword alignments
		switch (alignment) {
			case "top":
				alignValue = "10%";
				break;
			case "center":
			case "middle":
				alignValue = "50%";
				break;
			case "bottom":
				alignValue = "90%";
				break;
			case "header":
				alignValue = "15%";
				break;
			case "footer":
				alignValue = "85%";
				break;
			default:
				// Handle percentage or pixel values
				if (typeof alignment === "number") {
					alignValue = alignment + "%";
				} else if (typeof alignment === "string") {
					alignValue = alignment;
				} else {
					return false;
				}
		}

		ticker.style.setProperty("--vertical-align", alignValue);
		return true;
	}

	function getVerticalAlign() {
		if (!isInitialized || !ticker) {
			return null;
		}

		const computedStyle = getComputedStyle(ticker);
		return computedStyle.getPropertyValue("--vertical-align") || "30%";
	}

	function getConfig() {
		if (!isInitialized || !ticker) {
			return null;
		}

		const computedStyle = getComputedStyle(ticker);
		return {
			gap: parseInt(computedStyle.getPropertyValue("--gap")) || 32,
			textColor: computedStyle.getPropertyValue("--ticker-text-color") || "#FFFFFF",
			fontSize: computedStyle.getPropertyValue("--font-size") || "9vmin",
			fontFamily: computedStyle.getPropertyValue("--font-family") || "OpenSansSemiBold, sans-serif",
			fontWeight: computedStyle.getPropertyValue("--font-weight") || "600",
			verticalAlign: getVerticalAlign()
		};
	}

	return {
		init: init,
		setText: setText,
		getText: getText,
		recalculate: recalculate,
		destroy: destroy,
		isReady: isReady,
		getConfig: getConfig,
		setVerticalAlign: setVerticalAlign,
		getVerticalAlign: getVerticalAlign
	};
})();
