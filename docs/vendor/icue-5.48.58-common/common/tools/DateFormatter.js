/******************************************************************************
 **
 ** File      DateFormatter.js
 ** Author    Maksym Aldokhin
 ** Copyright (c) 2026, Corsair Memory, Inc. All Rights Reserved.
 **
 ** This file is part of Corsair iCUE Software.
 **
 ******************************************************************************/

class DateFormatter {
	constructor(locale = navigator.language, timeZone, date = new Date()) {
		this.date = new Date();
		this.locale = locale;
		this.timeZone = timeZone;
	}

	pad2(n) {
		return String(n).padStart(2, "0");
	}

	capitalizeFirst(str) {
		return str.charAt(0).toUpperCase() + str.slice(1);
	}

	localize(options) {
		return new Intl.DateTimeFormat(this.locale, { timeZone: this.timeZone, ...options }).format(this.date);
	}

	formatDDMMYYYY() {
		const day = this.pad2(this.localizePart("day"));
		const month = this.pad2(this.localizePart("month", "numeric"));
		const year = this.localizePart("year", "numeric");
		return `${day}/${month}/${year}`;
	}

	formatMMDDYYYY() {
		const month = this.pad2(this.localizePart("month", "numeric"));
		const day = this.pad2(this.localizePart("day"));
		const year = this.localizePart("year", "numeric");
		return `${month}/${day}/${year}`;
	}

	formatDD_MMM_YY() {
		const day = this.pad2(this.localizePart("day"));
		const monthShort = this.capitalizeFirst(this.localize({ month: "short" }));
		const year2 = String(this.localizePart("year", "2-digit")).padStart(2, "0");
		return `${day} ${monthShort} ${year2}`;
	}

	formatddd_D_MMM() {
		const weekdayShort = this.capitalizeFirst(this.localize({ weekday: "short" }));
		const day = this.localizePart("day");
		const monthShort = this.capitalizeFirst(this.localize({ month: "short" }));
		return `${weekdayShort} ${day} ${monthShort}`;
	}

	formatdddd_D_MMMM() {
		const weekdayLong = this.capitalizeFirst(this.localize({ weekday: "long" }));
		const day = this.localizePart("day");
		const monthLong = this.capitalizeFirst(this.localize({ month: "long" }));
		return `${weekdayLong} ${day} ${monthLong}`;
	}

	localizePart(type, value) {
		const options = { timeZone: this.timeZone };

		if (type === "day") {
			options.day = value || "numeric";
		} else if (type === "month") {
			options.month = value || "numeric";
		} else if (type === "year") {
			options.year = value || "numeric";
		} else if (type === "weekday") {
			options.weekday = value || "short";
		}

		const parts = new Intl.DateTimeFormat(this.locale, options).formatToParts(this.date);
		const part = parts.find((p) => p.type === type);
		return part ? part.value : "";
	}

	getDateText(dateText = "", lastDay = 0, forceUpdate = false) {
		const nowDate = new Date();

		// Check if the current day is the same as the last processed day to avoid unnecessary updates
		if (lastDay === nowDate.getDay() && !forceUpdate) {
			return Promise.resolve(undefined);
		}

		this.date = nowDate;

		switch (dateText) {
			case "None":
				return Promise.resolve("");
			case "04/05/2020":
				return Promise.resolve(this.formatMMDDYYYY());
			case "05/04/2020":
				return Promise.resolve(this.formatDDMMYYYY());
			case "05 Apr 20":
				return Promise.resolve(this.formatDD_MMM_YY());
			case "Sun 5 Apr":
				return Promise.resolve(this.formatddd_D_MMM());
			case "Sunday 5 April":
				return Promise.resolve(this.formatdddd_D_MMMM());
			case "System":
			default:
				return iCUE.formatUserLocaleDate(this.timeZone, this.locale);
		}
	}
}
