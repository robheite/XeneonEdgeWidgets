/******************************************************************************
 **
 ** File      SimpleFpsApiWrapper.js
 ** Author    Oleksandr Yaroshenko
 ** Copyright (c) 2026, Corsair Memory, Inc. All Rights Reserved.
 **
 ** This file is part of Corsair iCUE Software.
 **
 ******************************************************************************/

/**
 * FPS API
 *
 * FPS-specific wrapper that extends IcueWidgetApiWrapper.
 * Provides convenient methods for FPS data retrieval.
 *
 * For initialization use fpsplugin.
 *
 * Usage:
 *   const api = new SimpleFpsApiWrapper(fpsplugin);
 *   api.getCurrentFps().then(value => {
 *       console.log("Current FPS:", value);
 *   });
 *   api.getFpsAvailable().then(value => {
 *       console.log("FPS available:", value);
 *   });
 *   api.getCurrentProcess().then(value => {
 *       console.log("Current process:", value);
 *   });
 */

class SimpleFpsApiWrapper extends IcueWidgetApiWrapper {
	constructor(fpsPlugin, timeoutMs = 5000) {
		super(fpsPlugin, timeoutMs);
	}

	getCurrentFps() {
		return this.request(this.plugin.getCurrentFps);
	}

	getFpsAvailable() {
		return this.request(this.plugin.getFpsAvailable);
	}

	getCurrentProcess() {
		return this.request(this.plugin.getCurrentProcess);
	}
}
