import { CheckCircle2, AlertTriangle, XCircle } from "lucide-react";
import { Badge } from "../../shared/components/Badge";

interface HealthBadgeProps {
  status: "ready" | "warning" | "missing";
  label?: string;
}

export function HealthBadge({ status, label }: HealthBadgeProps) {
  if (status === "ready") {
    return (
      <Badge tone="success">
        <CheckCircle2 className="w-3.5 h-3.5" />
        {label ?? "Listo"}
      </Badge>
    );
  }

  if (status === "warning") {
    return (
      <Badge tone="warning">
        <AlertTriangle className="w-3.5 h-3.5" />
        {label ?? "Revisar"}
      </Badge>
    );
  }

  return (
    <Badge tone="error">
      <XCircle className="w-3.5 h-3.5" />
      {label ?? "No encontrado"}
    </Badge>
  );
}
