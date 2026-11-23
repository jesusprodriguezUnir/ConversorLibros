import { vi } from "vitest";

/** Minimal stand-in for HTMLAudioElement: state, play/pause events and the fields the reader touches. */
export class FakeAudio extends EventTarget {
  currentTime = 0;
  paused = true;
  playbackRate = 1;
  defaultPlaybackRate = 1;
  private source = "";
  play = vi.fn(async () => {
    this.paused = false;
    this.dispatchEvent(new Event("play"));
  });
  pause = vi.fn(() => {
    this.paused = true;
    this.dispatchEvent(new Event("pause"));
  });

  get src() {
    return this.source;
  }

  /** Mirrors the media load algorithm: rate back to default, paused without a `pause` event, position reset. */
  set src(value: string) {
    this.source = value;
    this.playbackRate = this.defaultPlaybackRate;
    this.paused = true;
    this.currentTime = 0;
  }
}
