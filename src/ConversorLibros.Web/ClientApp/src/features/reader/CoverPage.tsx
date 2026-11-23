import { useState } from "react";
import { api } from "../../shared/api/client";

export function CoverPage({ bookId, title }: { bookId: string; title: string | null }) {
  const [failed, setFailed] = useState(false);
  return (
    <figure className="reader-cover">
      {failed ? (
        <figcaption>{title ?? "Sin título"}</figcaption>
      ) : (
        <img
          src={api.libraryCoverUrl(bookId)}
          alt={title ? `Portada de ${title}` : "Portada del libro"}
          onError={() => setFailed(true)}
        />
      )}
    </figure>
  );
}
