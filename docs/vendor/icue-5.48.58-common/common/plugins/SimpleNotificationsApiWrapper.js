/******************************************************************************
 **
 ** File      SimpleNotificationsApiWrapper.js
 ** Author    Ihor Vashchyshyn
 ** Copyright (c) 2026, Corsair Memory, Inc. All Rights Reserved.
 **
 ** This file is part of Corsair iCUE Software.
 **
 ******************************************************************************/

/**
 * Notifications API
 *
 * Notifications-specific wrapper that extends IcueWidgetApiWrapper.
 * Provides convenient methods for notifications data retrieval.
 *
 * For initialization use notificationsplugin.
 *
 * Usage:
 *   const api = new SimpleNotificationsApiWrapper(notificationsplugin);
 *   api.getNotificationCount().then(value => {
 *       console.log("Notifications count value:", value);
 *   });
 */

class SimpleNotificationsApiWrapper extends IcueWidgetApiWrapper {
	constructor(notificationsPlugin, timeoutMs = 5000) {
		super(notificationsPlugin, timeoutMs);
	}

	getNotificationCount() {
		return this.request(this.plugin.getNotificationCount);
	}
}
