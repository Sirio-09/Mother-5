using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 4f;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 movement;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        // Raccoglie l'input (Funziona sia con il vecchio che con il nuovo Input System in modalità compatibile)
        movement.x = Input.GetAxisRaw("Horizontal");
        movement.y = Input.GetAxisRaw("Vertical");

        // Evita che il personaggio si muova più velocemente in diagonale
        if (movement.magnitude > 1)
        {
            movement = movement.normalized;
        }

        // Passa i parametri all'Animator solo se ci si sta muovendo
        // In questo modo il PG ricorderà l'ultima direzione quando si ferma (stile JRPG)
        if (movement != Vector2.zero)
        {
            animator.SetFloat("MoveX", movement.x);
            animator.SetFloat("MoveY", movement.y);
            animator.SetBool("IsMoving", true);
        }
        else
        {
            animator.SetBool("IsMoving", false);
        }
    }

    void FixedUpdate()
    {
        // Applica il movimento basato sulla fisica del Rigidbody
        rb.MovePosition(rb.position + movement * moveSpeed * Time.fixedDeltaTime);
    }
}