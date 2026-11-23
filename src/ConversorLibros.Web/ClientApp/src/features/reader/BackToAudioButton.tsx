export function BackToAudioButton({ onClick }: { onClick: () => void }) {
  return (
    <button type="button" className="reader-back-to-audio" onClick={onClick}>
      Volver al audio
    </button>
  );
}
