/******************************************************************************
 **
 ** File      SimpleMediaApiWrapper.js
 ** Author    Maksym Aldokhin
 ** Copyright (c) 2026, Corsair Memory, Inc. All Rights Reserved.
 **
 ** This file is part of Corsair iCUE Software.
 **
 ******************************************************************************/

/**
 * Media API
 *
 * Media-specific wrapper that extends IcueWidgetApiWrapper.
 * Provides convenient methods for sensor data retrieval.
 *
 * For initialization use mediaplugin.
 *
 * Usage:
 *   const api = new SimpleMediaApiWrapper(mediaplugin);
 *   api.getSongName().then(value => {
 *       console.log("Song name value:", value);
 *   });
 *   api.getArtist().then(value => {
 *       console.log("Artist value:", value);
 *   });
 */

class SimpleMediaApiWrapper extends IcueWidgetApiWrapper {
	constructor(mediaPlugin, timeoutMs = 5000) {
		super(mediaPlugin, timeoutMs);
	}

	getSongName() {
		return this.request(this.plugin.getSongName);
	}

	getArtist() {
		return this.request(this.plugin.getArtist);
	}
}
