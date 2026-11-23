import { useCallback, useRef, useState } from "react";

/** Auto-follow flag: `followingRef` is read by the rAF loop, `following` drives the UI. */
export function useAutoFollow() {
  const followingRef = useRef(true);
  const [following, setFollowing] = useState(true);
  const set = useCallback((value: boolean) => {
    followingRef.current = value;
    setFollowing(value);
  }, []);
  const suspend = useCallback(() => set(false), [set]);
  const resume = useCallback(() => set(true), [set]);
  return { following, followingRef, suspend, resume };
}
