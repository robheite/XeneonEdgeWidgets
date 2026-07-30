/******************************************************************************
 **
 ** File      MediaViewer.js
 ** Author    Maksym Aldokhin
 ** Copyright (c) 2026, Corsair Memory, Inc. All Rights Reserved.
 **
 ** This file is part of Corsair iCUE Software.
 **
 ******************************************************************************/

class MediaViewer {
	constructor(options = {}) {
		this.container = options.container;
		this.onMediaLoaded = options.onMediaLoaded || (() => {});
		this.onMediaError = options.onMediaError || (() => {});
		this.debug = options.debug || false;

		this.positionContainer = null;
		this.scaleContainer = null;
		this.rotationContainer = null;
		this.mediaElement = null;
		this.currentMediaPath = null;

		this.supportedImageFormats = ["jpg", "jpeg", "png", "gif", "bmp", "webp", "svg", "ico"];
		this.supportedVideoFormats = ["mp4", "webm", "ogg", "mov", "avi", "mkv"];

		if (!this.container) {
			throw new Error("MediaViewer: container is required");
		}
	}

	getFileExtension(filePath) {
		return filePath.split(".").pop().toLowerCase();
	}

	isImageFile(ext) {
		return this.supportedImageFormats.includes(ext);
	}

	isVideoFile(ext) {
		return this.supportedVideoFormats.includes(ext);
	}

	log(...args) {
		if (this.debug) {
			console.log("[MediaViewer]", ...args);
		}
	}

	warn(...args) {
		if (this.debug) {
			console.warn("[MediaViewer]", ...args);
		}
	}

	error(...args) {
		console.error("[MediaViewer]", ...args);
	}

	createImageElement(filePath) {
		const img = document.createElement("img");
		img.src = filePath;

		img.onerror = () => {
			this.error("Failed to load image:", filePath);
			this.onMediaError(new Error(`Failed to load image: ${filePath}`));
		};

		img.onload = () => {
			this.log("Image loaded successfully:", filePath);
			this.log("Image dimensions:", img.naturalWidth, "x", img.naturalHeight);
			this.onMediaLoaded(img);
		};

		return img;
	}

	createVideoElement(filePath) {
		const video = document.createElement("video");
		video.controls = false;
		video.autoplay = true;
		video.loop = true;
		video.muted = true;
		video.playsInline = true;

		video.addEventListener("loadedmetadata", () => {
			this.log("Video metadata loaded:", filePath);
			this.log("Video dimensions:", video.videoWidth, "x", video.videoHeight);
		});

		video.addEventListener("loadeddata", () => {
			this.log("Video data loaded, attempting to play");
			video
				.play()
				.then(() => {
					this.log("Video playing successfully");
					this.onMediaLoaded(video);
				})
				.catch((error) => {
					this.error("Error playing video:", error);
					this.onMediaError(error);
				});
		});

		video.addEventListener("error", (e) => {
			this.error("Video error:", e);
			if (video.error) {
				this.error("Error code:", video.error.code);
				this.error("Error message:", video.error.message);
			}
			this.onMediaError(new Error(`Video error: ${video.error?.message || "Unknown error"}`));
		});

		video.src = filePath;
		return video;
	}

	createMediaElement(filePath) {
		const ext = this.getFileExtension(filePath);

		if (this.isImageFile(ext)) {
			return this.createImageElement(filePath);
		} else if (this.isVideoFile(ext)) {
			return this.createVideoElement(filePath);
		} else {
			this.error("Unsupported media format:", ext);
			this.onMediaError(new Error(`Unsupported media format: ${ext}`));
			return null;
		}
	}
	createContainers() {
		this.positionContainer = document.createElement("div");
		this.positionContainer.className = "position-container";

		this.scaleContainer = document.createElement("div");
		this.scaleContainer.className = "scale-container";

		this.rotationContainer = document.createElement("div");
		this.rotationContainer.className = "rotation-container";

		this.rotationContainer.appendChild(this.mediaElement);
		this.scaleContainer.appendChild(this.rotationContainer);
		this.positionContainer.appendChild(this.scaleContainer);

		return this.positionContainer;
	}

	applyTransform(params = {}) {
		if (!this.positionContainer || !this.scaleContainer || !this.rotationContainer) {
			this.warn("Containers not initialized, cannot apply transform");
			return;
		}

		const baseWidth = params.baseWidth || 100;
		const baseHeight = params.baseHeight || 100;
		const scaleValue = params.scale !== undefined ? params.scale : 1;
		const posX = params.positionX || 0;
		const posY = params.positionY || 0;
		const rotation = params.angle || 0;

		const containerWidth = this.container.clientWidth;
		const containerHeight = this.container.clientHeight;

		const scaleX = containerWidth / baseWidth;
		const scaleY = containerHeight / baseHeight;
		const minScale = Math.min(scaleX, scaleY);

		const fixedPosX = posX * minScale;
		const fixedPosY = posY * minScale;
		const fixedScale = scaleValue * minScale;

		this.positionContainer.style.transform = `translate(${fixedPosX}px, ${fixedPosY}px)`;
		this.scaleContainer.style.transform = `scale(${fixedScale})`;
		this.rotationContainer.style.transform = `rotate(${rotation}deg)`;
	}

	loadMedia(params = {}) {
		const filePath = params.path;

		if (!filePath) {
			this.clear();
			return;
		}

		const filePathDecoded = decodeURIComponent(filePath);

		if (this.currentMediaPath === filePathDecoded) {
			this.log("Same media, only updating transform");
			this.applyTransform(params);
			return;
		}

		this.log("Loading new media:", filePathDecoded);
		this.clear();

		this.mediaElement = this.createMediaElement(filePathDecoded);
		if (!this.mediaElement) {
			this.error("Failed to create media element");
			return;
		}

		const containerElement = this.createContainers();
		this.container.appendChild(containerElement);
		this.container.style.visibility = "visible";

		this.applyTransform(params);
		this.currentMediaPath = filePathDecoded;
	}

	clear() {
		this.container.innerHTML = "";
		this.container.style.visibility = "hidden";
		this.positionContainer = null;
		this.scaleContainer = null;
		this.rotationContainer = null;
		this.mediaElement = null;
		this.currentMediaPath = null;
	}

	destroy() {
		this.clear();
		this.container = null;
		this.onMediaLoaded = null;
		this.onMediaError = null;
	}
}
